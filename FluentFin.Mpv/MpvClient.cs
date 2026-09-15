using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Concurrent;
using FluentFin.Mpv.Interop;

namespace FluentFin.Mpv;

public sealed class MpvClient : IAsyncDisposable
{
	private readonly MpvOptions _options;
	private readonly CancellationTokenSource _eventLoopCts = new();
	private readonly ConcurrentDictionary<ulong, PendingCommand> _pendingCommands = [];
	private SafeMpvHandle? _handle;
	private Task? _eventLoopTask;
	private long _nextReplyUserData = 1000;
	private bool _initialized;

	public MpvClient(MpvOptions? options = null)
	{
		_options = options ?? new MpvOptions();
	}

	public bool Paused
	{
		get => GetPropertyFlag("pause");
		set => SetPropertyFlag("pause", value);
	}

	public double Volume
	{
		get => GetPropertyDouble("volume");
		set => SetPropertyDouble("volume", Math.Clamp(value, 0, 100));
	}

	public bool Muted
	{
		get => GetPropertyFlag("mute");
		set => SetPropertyFlag("mute", value);
	}

	public TimeSpan Position => TimeSpan.FromSeconds(Math.Max(0, GetPropertyDouble("time-pos")));
	public TimeSpan Duration => TimeSpan.FromSeconds(Math.Max(0, GetPropertyDouble("duration")));
	public nint DisplaySwapChain => (nint)GetPropertyInt64("display-swapchain");

	public event EventHandler? FileLoaded;
	public event EventHandler<MpvEndFileEventArgs>? EndFile;
	public event EventHandler<MpvPropertyChangedEventArgs>? PropertyChanged;
	public event EventHandler? VideoReconfigured;
	public event EventHandler? PlaybackRestarted;

	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		if (_initialized)
		{
			return;
		}

		cancellationToken.ThrowIfCancellationRequested();
		_handle = new SafeMpvHandle();
		if (_handle.IsInvalid)
		{
			throw new MpvException(-1, "mpv_create returned a null handle.");
		}

		SetPropertyString("vo", _options.VideoOutput);
		SetPropertyString("gpu-api", _options.GpuApi);
		SetPropertyString("gpu-context", _options.GpuContext);
		SetPropertyString("d3d11-output-mode", _options.D3D11OutputMode);
		SetPropertyString("hwdec", _options.HardwareDecoding);
		SetPropertyString("terminal", "no");

		Check(MpvNative.Initialize(_handle.DangerousGetHandle()), "initialize mpv");
		_initialized = true;

		Observe("pause", MpvFormat.Flag, 1);
		Observe("time-pos", MpvFormat.Double, 2);
		Observe("duration", MpvFormat.Double, 3);
		Observe("track-list", MpvFormat.None, 4);
		Observe("display-swapchain", MpvFormat.Int64, 5);

		_eventLoopTask = Task.Run(RunEventLoop);
		await Task.CompletedTask;
	}

	public Task LoadAsync(string url, CancellationToken cancellationToken = default) => CommandAsync(cancellationToken, "loadfile", url, "replace");
	public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) => CommandAsync(cancellationToken, "seek", position.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), "absolute");
	public Task StopAsync(CancellationToken cancellationToken = default) => CommandAsync(cancellationToken, "stop");
	public Task CommandAsync(params string[] arguments) => CommandAsync(CancellationToken.None, arguments);

	public Task CommandAsync(CancellationToken cancellationToken, params string[] arguments)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfNotInitialized();
		var replyUserData = (ulong)Interlocked.Increment(ref _nextReplyUserData);
		var operation = arguments.FirstOrDefault() ?? "command";
		var pending = new PendingCommand(operation);
		_pendingCommands[replyUserData] = pending;
		if (cancellationToken.CanBeCanceled)
		{
			pending.CancellationRegistration = cancellationToken.Register(() =>
			{
				if (_pendingCommands.TryRemove(replyUserData, out var command))
				{
					command.Cancel();
				}
			});
		}

		unsafe
		{
			var utf8 = arguments.Select(arg => Encoding.UTF8.GetBytes(arg + "\0")).ToArray();
			var handles = utf8.Select(bytes => GCHandle.Alloc(bytes, GCHandleType.Pinned)).ToArray();
			try
			{
				var pointers = new nint[handles.Length + 1];
				for (var i = 0; i < handles.Length; i++)
				{
					pointers[i] = handles[i].AddrOfPinnedObject();
				}

				fixed (nint* ptr = pointers)
				{
					var result = MpvNative.CommandAsync(_handle!.DangerousGetHandle(), replyUserData, (nint)ptr);
					if (result < 0 && _pendingCommands.TryRemove(replyUserData, out var failedCommand))
					{
						failedCommand.Fail(new MpvException(result, $"Unable to queue mpv command {operation}: {MpvNative.ErrorString(result)}"));
					}
				}
			}
			finally
			{
				foreach (var handle in handles)
				{
					handle.Free();
				}
			}
		}

		return pending.Task;
	}

	public IReadOnlyList<MpvTrack> GetTracks()
	{
		var count = (int)Math.Max(0, GetPropertyInt64("track-list/count"));
		var tracks = new List<MpvTrack>(count);
		for (var i = 0; i < count; i++)
		{
			var id = GetPropertyInt64($"track-list/{i}/id");
			var ffIndex = TryGetPropertyInt64($"track-list/{i}/ff-index");
			var type = GetPropertyString($"track-list/{i}/type") ?? "";
			var language = GetPropertyString($"track-list/{i}/lang");
			var title = GetPropertyString($"track-list/{i}/title");
			if (id > 0 && !string.IsNullOrWhiteSpace(type))
			{
				tracks.Add(new MpvTrack(id, ffIndex, type, language, title));
			}
		}

		return tracks;
	}

	public string? GetPropertyString(string name)
	{
		ThrowIfNotInitialized();
		unsafe
		{
			nint ptr = nint.Zero;
			try
			{
				var result = MpvNative.GetProperty(_handle!.DangerousGetHandle(), name, MpvFormat.String, (nint)(&ptr));
				if (result < 0)
				{
					return null;
				}

				return ptr == nint.Zero ? null : Marshal.PtrToStringUTF8(ptr);
			}
			finally
			{
				if (ptr != nint.Zero)
				{
					MpvNative.Free(ptr);
				}
			}
		}
	}

	public void SetPropertyString(string name, string value)
	{
		ThrowIfHandleMissing();
		Check(MpvNative.SetPropertyString(_handle!.DangerousGetHandle(), name, value), $"set mpv property {name}");
	}

	public long GetPropertyInt64(string name)
	{
		ThrowIfNotInitialized();
		unsafe
		{
			long value = 0;
			var result = MpvNative.GetProperty(_handle!.DangerousGetHandle(), name, MpvFormat.Int64, (nint)(&value));
			return result < 0 ? 0 : value;
		}
	}

	public long? TryGetPropertyInt64(string name)
	{
		ThrowIfNotInitialized();
		unsafe
		{
			long value = 0;
			var result = MpvNative.GetProperty(_handle!.DangerousGetHandle(), name, MpvFormat.Int64, (nint)(&value));
			return result < 0 ? null : value;
		}
	}

	public void SetPropertyInt64(string name, long value)
	{
		ThrowIfNotInitialized();
		unsafe
		{
			Check(MpvNative.SetProperty(_handle!.DangerousGetHandle(), name, MpvFormat.Int64, (nint)(&value)), $"set mpv property {name}");
		}
	}

	public double GetPropertyDouble(string name)
	{
		ThrowIfNotInitialized();
		unsafe
		{
			double value = 0;
			var result = MpvNative.GetProperty(_handle!.DangerousGetHandle(), name, MpvFormat.Double, (nint)(&value));
			return result < 0 ? 0 : value;
		}
	}

	public void SetPropertyDouble(string name, double value)
	{
		ThrowIfNotInitialized();
		unsafe
		{
			Check(MpvNative.SetProperty(_handle!.DangerousGetHandle(), name, MpvFormat.Double, (nint)(&value)), $"set mpv property {name}");
		}
	}

	public bool GetPropertyFlag(string name)
	{
		ThrowIfNotInitialized();
		unsafe
		{
			int value = 0;
			var result = MpvNative.GetProperty(_handle!.DangerousGetHandle(), name, MpvFormat.Flag, (nint)(&value));
			return result >= 0 && value != 0;
		}
	}

	public void SetPropertyFlag(string name, bool value)
	{
		ThrowIfNotInitialized();
		unsafe
		{
			var flag = value ? 1 : 0;
			Check(MpvNative.SetProperty(_handle!.DangerousGetHandle(), name, MpvFormat.Flag, (nint)(&flag)), $"set mpv property {name}");
		}
	}

	public async ValueTask DisposeAsync()
	{
		_eventLoopCts.Cancel();
		if (_eventLoopTask is not null)
		{
			try
			{
				await _eventLoopTask.ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
			}
		}

		_handle?.Dispose();
		_eventLoopCts.Dispose();
		foreach (var (_, pending) in _pendingCommands.ToArray())
		{
			pending.Cancel();
		}

		_pendingCommands.Clear();
	}

	private void Observe(string name, MpvFormat format, ulong id)
	{
		Check(MpvNative.ObserveProperty(_handle!.DangerousGetHandle(), id, name, format), $"observe mpv property {name}");
	}

	private void RunEventLoop()
	{
		while (!_eventLoopCts.IsCancellationRequested && _handle is not null && !_handle.IsInvalid)
		{
			var eventPtr = MpvNative.WaitEvent(_handle.DangerousGetHandle(), 0.1);
			if (eventPtr == nint.Zero)
			{
				continue;
			}

			var mpvEvent = Marshal.PtrToStructure<MpvEvent>(eventPtr);
			switch (mpvEvent.EventId)
			{
				case MpvEventId.None:
					break;
				case MpvEventId.CommandReply:
					HandleCommandReply(mpvEvent);
					break;
				case MpvEventId.FileLoaded:
					FileLoaded?.Invoke(this, EventArgs.Empty);
					break;
				case MpvEventId.EndFile:
					var end = Marshal.PtrToStructure<MpvEventEndFile>(mpvEvent.Data);
					EndFile?.Invoke(this, new MpvEndFileEventArgs(end.Reason, end.Error));
					break;
				case MpvEventId.PropertyChange:
					HandlePropertyChange(mpvEvent.Data);
					break;
				case MpvEventId.VideoReconfig:
					VideoReconfigured?.Invoke(this, EventArgs.Empty);
					break;
				case MpvEventId.PlaybackRestart:
					PlaybackRestarted?.Invoke(this, EventArgs.Empty);
					break;
				case MpvEventId.Shutdown:
					CancelPendingCommands();
					return;
			}
		}
	}

	private void HandleCommandReply(MpvEvent mpvEvent)
	{
		if (!_pendingCommands.TryRemove(mpvEvent.ReplyUserData, out var pending))
		{
			return;
		}

		if (mpvEvent.Error < 0)
		{
			pending.Fail(new MpvException(mpvEvent.Error, $"Unable to execute mpv command {pending.Operation}: {MpvNative.ErrorString(mpvEvent.Error)}"));
			return;
		}

		pending.Complete();
	}

	private void CancelPendingCommands()
	{
		foreach (var (key, pending) in _pendingCommands.ToArray())
		{
			if (_pendingCommands.TryRemove(key, out _))
			{
				pending.Cancel();
			}
		}
	}

	private void HandlePropertyChange(nint data)
	{
		if (data == nint.Zero)
		{
			return;
		}

		var property = Marshal.PtrToStructure<MpvEventProperty>(data);
		var name = Marshal.PtrToStringUTF8(property.Name);
		if (string.IsNullOrWhiteSpace(name))
		{
			return;
		}

		object? value = null;
		unsafe
		{
			value = property.Format switch
			{
				MpvFormat.Flag => property.Data == nint.Zero ? null : *(int*)property.Data != 0,
				MpvFormat.Double => property.Data == nint.Zero ? null : *(double*)property.Data,
				MpvFormat.Int64 => property.Data == nint.Zero ? null : *(long*)property.Data,
				MpvFormat.String => property.Data == nint.Zero ? null : Marshal.PtrToStringUTF8(*(nint*)property.Data),
				_ => null
			};
		}

		PropertyChanged?.Invoke(this, new MpvPropertyChangedEventArgs(name, value));
	}

	private void ThrowIfHandleMissing()
	{
		if (_handle is null || _handle.IsInvalid)
		{
			throw new ObjectDisposedException(nameof(MpvClient));
		}
	}

	private void ThrowIfNotInitialized()
	{
		ThrowIfHandleMissing();
		if (!_initialized)
		{
			throw new InvalidOperationException("mpv has not been initialized.");
		}
	}

	private static void Check(int error, string operation)
	{
		if (error < 0)
		{
			throw new MpvException(error, $"Unable to {operation}: {MpvNative.ErrorString(error)}");
		}
	}

	private sealed class PendingCommand(string operation)
	{
		private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public string Operation { get; } = operation;
		public CancellationTokenRegistration CancellationRegistration { get; set; }
		public Task Task => _completion.Task;

		public void Complete()
		{
			CancellationRegistration.Dispose();
			_completion.TrySetResult();
		}

		public void Fail(Exception exception)
		{
			CancellationRegistration.Dispose();
			_completion.TrySetException(exception);
		}

		public void Cancel()
		{
			CancellationRegistration.Dispose();
			_completion.TrySetCanceled();
		}
	}
}
