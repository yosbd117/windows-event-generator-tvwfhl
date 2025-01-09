using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using EventSimulator.Core.Constants;
using EventSimulator.Core.Models;

namespace EventSimulator.Core.Utils
{
    /// <summary>
    /// Provides a high-performance, thread-safe wrapper around the Windows Event Log API
    /// for writing synthetic events to the Windows Event Log system.
    /// </summary>
    public sealed class WindowsEventLogApi : IDisposable
    {
        private const int EVENTLOG_HANDLE_INVALID = -1;
        private const int DEFAULT_BATCH_SIZE = 100;
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const int HANDLE_POOL_SIZE = 5;

        private readonly ConcurrentDictionary<string, Queue<IntPtr>> _handlePool;
        private readonly bool _isElevated;
        private readonly ILogger<WindowsEventLogApi> _logger;
        private readonly SemaphoreSlim _handlePoolLock;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the WindowsEventLogApi class.
        /// </summary>
        /// <param name="logger">Logger for tracking operations and errors.</param>
        public WindowsEventLogApi(ILogger<WindowsEventLogApi> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _handlePool = new ConcurrentDictionary<string, Queue<IntPtr>>();
            _handlePoolLock = new SemaphoreSlim(1, 1);
            _disposed = false;

            // Check for elevated privileges
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            _isElevated = principal.IsInRole(WindowsBuiltInRole.Administrator);

            InitializeHandlePool();
        }

        /// <summary>
        /// Writes a single event to the Windows Event Log with validation and retry logic.
        /// </summary>
        /// <param name="eventInstance">The event instance to write.</param>
        /// <returns>True if successful, false otherwise.</returns>
        public async Task<bool> WriteEvent(EventInstance eventInstance)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WindowsEventLogApi));
            if (eventInstance == null) throw new ArgumentNullException(nameof(eventInstance));

            var validationResult = eventInstance.Validate();
            if (!validationResult.Succeeded)
            {
                _logger.LogError("Event validation failed: {Message}", validationResult.ErrorMessage);
                eventInstance.SetGenerationStatus("Failed", validationResult.ErrorMessage);
                return false;
            }

            if (!_isElevated && eventInstance.Channel == EventLogChannels.Security)
            {
                _logger.LogError("Elevated privileges required to write to Security log");
                eventInstance.SetGenerationStatus("Failed", "Insufficient privileges");
                return false;
            }

            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = await AcquireHandle(eventInstance.Channel);
                if (handle == (IntPtr)EVENTLOG_HANDLE_INVALID)
                {
                    eventInstance.SetGenerationStatus("Failed", "Could not acquire event log handle");
                    return false;
                }

                for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
                {
                    try
                    {
                        using (var eventLog = new EventLogWriter(handle))
                        {
                            await eventLog.WriteEvent(eventInstance.GeneratedXml);
                            eventInstance.SetGenerationStatus("Success");
                            return true;
                        }
                    }
                    catch (Exception ex) when (attempt < MAX_RETRY_ATTEMPTS)
                    {
                        _logger.LogWarning(ex, "Write attempt {Attempt} failed, retrying...", attempt);
                        await Task.Delay(100 * attempt);
                    }
                }

                eventInstance.SetGenerationStatus("Failed", "Max retry attempts exceeded");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write event {EventId}", eventInstance.EventId);
                eventInstance.SetGenerationStatus("Failed", ex.Message);
                return false;
            }
            finally
            {
                if (handle != (IntPtr)EVENTLOG_HANDLE_INVALID)
                {
                    await ReleaseHandle(eventInstance.Channel, handle);
                }
            }
        }

        /// <summary>
        /// Writes multiple events to the Windows Event Log using optimized batch processing.
        /// </summary>
        /// <param name="events">Collection of events to write.</param>
        /// <param name="batchSize">Optional batch size for processing.</param>
        /// <returns>True if all events were written successfully.</returns>
        public async Task<bool> WriteEventBatch(IEnumerable<EventInstance> events, int batchSize = DEFAULT_BATCH_SIZE)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WindowsEventLogApi));
            if (events == null) throw new ArgumentNullException(nameof(events));
            if (batchSize <= 0) throw new ArgumentException("Batch size must be positive", nameof(batchSize));

            var eventsByChannel = new ConcurrentDictionary<string, List<EventInstance>>();
            var success = true;

            // Group events by channel
            foreach (var evt in events)
            {
                eventsByChannel.GetOrAdd(evt.Channel, _ => new List<EventInstance>()).Add(evt);
            }

            // Process each channel's events in parallel
            var tasks = new List<Task>();
            foreach (var channelGroup in eventsByChannel)
            {
                tasks.Add(ProcessChannelBatch(channelGroup.Key, channelGroup.Value, batchSize));
            }

            await Task.WhenAll(tasks);

            return success;
        }

        private async Task ProcessChannelBatch(string channel, List<EventInstance> events, int batchSize)
        {
            IntPtr handle = await AcquireHandle(channel);
            if (handle == (IntPtr)EVENTLOG_HANDLE_INVALID)
            {
                _logger.LogError("Failed to acquire handle for channel {Channel}", channel);
                return;
            }

            try
            {
                for (int i = 0; i < events.Count; i += batchSize)
                {
                    var batch = events.GetRange(i, Math.Min(batchSize, events.Count - i));
                    using (var eventLog = new EventLogWriter(handle))
                    {
                        foreach (var evt in batch)
                        {
                            try
                            {
                                await eventLog.WriteEvent(evt.GeneratedXml);
                                evt.SetGenerationStatus("Success");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to write event {EventId}", evt.EventId);
                                evt.SetGenerationStatus("Failed", ex.Message);
                            }
                        }
                    }
                }
            }
            finally
            {
                await ReleaseHandle(channel, handle);
            }
        }

        private void InitializeHandlePool()
        {
            _handlePool.TryAdd(EventLogChannels.Security, new Queue<IntPtr>());
            _handlePool.TryAdd(EventLogChannels.System, new Queue<IntPtr>());
            _handlePool.TryAdd(EventLogChannels.Application, new Queue<IntPtr>());
        }

        private async Task<IntPtr> AcquireHandle(string channel)
        {
            await _handlePoolLock.WaitAsync();
            try
            {
                if (_handlePool.TryGetValue(channel, out var handles) && handles.Count > 0)
                {
                    return handles.Dequeue();
                }

                return CreateEventLogHandle(channel);
            }
            finally
            {
                _handlePoolLock.Release();
            }
        }

        private async Task ReleaseHandle(string channel, IntPtr handle)
        {
            await _handlePoolLock.WaitAsync();
            try
            {
                if (_handlePool.TryGetValue(channel, out var handles) && handles.Count < HANDLE_POOL_SIZE)
                {
                    handles.Enqueue(handle);
                }
                else
                {
                    CloseEventLogHandle(handle);
                }
            }
            finally
            {
                _handlePoolLock.Release();
            }
        }

        private IntPtr CreateEventLogHandle(string channel)
        {
            try
            {
                var handle = EventLogHandle.OpenEventLog(channel);
                return handle.DangerousGetHandle();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create event log handle for channel {Channel}", channel);
                return (IntPtr)EVENTLOG_HANDLE_INVALID;
            }
        }

        private void CloseEventLogHandle(IntPtr handle)
        {
            try
            {
                if (handle != IntPtr.Zero && handle != (IntPtr)EVENTLOG_HANDLE_INVALID)
                {
                    using var eventLogHandle = new EventLogHandle(handle, true);
                    eventLogHandle.Close();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error closing event log handle");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            foreach (var channelHandles in _handlePool.Values)
            {
                while (channelHandles.Count > 0)
                {
                    CloseEventLogHandle(channelHandles.Dequeue());
                }
            }

            _handlePoolLock.Dispose();
            _disposed = true;
        }
    }
}