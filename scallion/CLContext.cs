﻿using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Compute.CL10;
using System.Text;
using System.Runtime.InteropServices;

namespace scallion
{
	public unsafe class CLContext
	{
		public readonly IntPtr DeviceId;
		public readonly CLDeviceInfo Device;
		public readonly IntPtr ContextId;
		public readonly IntPtr CommandQueueId;
		public unsafe CLContext(IntPtr deviceId)
		{
			DeviceId = deviceId;
			Device = new CLDeviceInfo(DeviceId);
			ErrorCode error;
			ErrorCode[] errors = new ErrorCode[1];
			ContextId = CL.CreateContext(null, 1, new IntPtr[] { DeviceId }, IntPtr.Zero, IntPtr.Zero, errors);
			if (errors[0] != ErrorCode.Success) throw new System.InvalidOperationException("Error calling CreateContext");
			CommandQueueId = CL.CreateCommandQueue(ContextId, DeviceId, (CommandQueueFlags)0, &error);
			if (error != ErrorCode.Success) throw new System.InvalidOperationException("Error calling CreateCommandQueue");
		}
		public IntPtr CreateAndCompileProgram(string source)
		{
			ErrorCode error;
			IntPtr programId;
			programId = CL.CreateProgramWithSource(ContextId, 1, new string[] { source }, null, &error);
			if (error != ErrorCode.Success) throw new System.InvalidOperationException("Error calling CreateProgramWithSource");
			error = (ErrorCode)CL.BuildProgram(programId, 0, (IntPtr[])null, null, IntPtr.Zero, IntPtr.Zero);
			if (error != ErrorCode.Success)
			{
				uint parmSize;
				CL.GetProgramBuildInfo(programId, DeviceId, ProgramBuildInfo.ProgramBuildLog, IntPtr.Zero, IntPtr.Zero, (IntPtr*)&parmSize);
				byte[] value = new byte[parmSize];
				fixed (byte* valuePtr = value)
				{
					error = (ErrorCode)CL.GetProgramBuildInfo(programId, DeviceId, ProgramBuildInfo.ProgramBuildLog, new IntPtr(&parmSize), new IntPtr(valuePtr), (IntPtr*)IntPtr.Zero.ToPointer());
				}
				if (error != ErrorCode.Success) throw new System.InvalidOperationException("Error calling GetProgramBuildInfo");
				throw new System.InvalidOperationException(Encoding.ASCII.GetString(value).Trim('\0'));
			}
			return programId;
		}
		public CLKernel CreateKernel(IntPtr programId, string kernelName)
		{
			return new CLKernel(DeviceId, ContextId, CommandQueueId, programId, kernelName);
		}
		public CLBuffer<T> CreateBuffer<T>(MemFlags memFlags, T[] data) where T : struct
		{
			return new CLBuffer<T>(ContextId, CommandQueueId, memFlags, data);
		}
	}
	public unsafe class CLBuffer<T> : IDisposable where T : struct
	{
		public readonly GCHandle Handle;
		public readonly IntPtr BufferId;
		public readonly IntPtr CommandQueueId;
		public readonly bool IsDevice64Bit;
		public readonly int BufferSize;
		public CLBuffer(IntPtr contextId, IntPtr commandQueueId, MemFlags memFlags, T[] data)
		{
			CommandQueueId = commandQueueId;
			Handle = GCHandle.Alloc(data, GCHandleType.Pinned);
			ErrorCode error = ErrorCode.Success;
			BufferSize = Marshal.SizeOf(typeof(T)) * data.Length;
			BufferId = CL.CreateBuffer(contextId, memFlags, new IntPtr(BufferSize), Handle.AddrOfPinnedObject(), &error);
			if (error != ErrorCode.Success) throw new System.InvalidOperationException("Error calling CreateBuffer");
		}

		public void EnqueueWrite()
		{
			ErrorCode error;
			error = (ErrorCode)CL.EnqueueWriteBuffer(CommandQueueId, BufferId, true, new IntPtr(0), new IntPtr(BufferSize), 
				Handle.AddrOfPinnedObject(), 0, (IntPtr*)IntPtr.Zero.ToPointer(), (IntPtr*)IntPtr.Zero.ToPointer());
			if (error != ErrorCode.Success) throw new System.InvalidOperationException("Error calling EnqueueWriteBuffer");
		}

		public void EnqueueRead()
		{
			ErrorCode error;
			error = (ErrorCode)CL.EnqueueReadBuffer(CommandQueueId, BufferId, true, new IntPtr(0), new IntPtr(BufferSize),
				Handle.AddrOfPinnedObject(), 0, (IntPtr*)IntPtr.Zero.ToPointer(), (IntPtr*)IntPtr.Zero.ToPointer());
			if (error != ErrorCode.Success) throw new System.InvalidOperationException("Error calling EnqueueReadBuffer");
		}

		private bool disposed = false;
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}
		protected virtual void Dispose(bool disposing)
		{
			if (!this.disposed)
			{
				if (disposing) { /*Dispose managed resources*/ }
				// Dispose unmanaged resources.
				CL.ReleaseMemObject(BufferId);
				Handle.Free();
			}
		}
		~CLBuffer()
        {
            Dispose(false);
        }
	}
	public unsafe class CLKernel
	{
		public readonly IntPtr KernelId;
		public readonly IntPtr ContextId;
		public readonly IntPtr CommandQueueId;
		public readonly IntPtr ProgramId;
		public readonly string KernelName;
		public readonly IntPtr DeviceId;
		public CLKernel(IntPtr deviceId, IntPtr contextId, IntPtr commandQueueId, IntPtr programId, string kernelName)
		{
			DeviceId = deviceId;
			ContextId = contextId;
			CommandQueueId = commandQueueId;
			ProgramId = programId;
			KernelName = kernelName;

			ErrorCode error;
			KernelId = CL.CreateKernel(ProgramId, KernelName, out error);
			if (error != ErrorCode.Success) throw new System.InvalidOperationException("Error calling CreateKernel");
		}
		public void EnqueueNDRangeKernel(int globalWorkSize, int localWorkSize)
		{
			ErrorCode error;
			IntPtr pglobalWorkSize = new IntPtr(globalWorkSize);
			IntPtr plocalWorkSize = new IntPtr(localWorkSize);
			error = (ErrorCode)CL.EnqueueNDRangeKernel(CommandQueueId, KernelId, 1, null, &pglobalWorkSize, &plocalWorkSize, 0, null, null);
			if (error != ErrorCode.Success) throw new System.InvalidOperationException("Error calling EnqueueNDRangeKernel");
		}
		public void SetKernelArgLocal(int argIndex, int size)
		{
			ErrorCode error;
			error = (ErrorCode)CL.SetKernelArg(KernelId, argIndex, new IntPtr(size), IntPtr.Zero);
			if (error != ErrorCode.Success) throw new System.InvalidOperationException("Error calling SetKernelArg");
		}
		public void SetKernelArg<T>(int argIndex, T value) where T : struct
		{
			ErrorCode error;
			var handle = GCHandle.Alloc(value, GCHandleType.Pinned);
			int size = Marshal.SizeOf(typeof(T));
			error = (ErrorCode)CL.SetKernelArg(KernelId, argIndex, new IntPtr(size), handle.AddrOfPinnedObject());
			handle.Free();
			if (error != ErrorCode.Success) throw new System.InvalidOperationException("Error calling SetKernelArg");
		}
		public void SetKernelArg<T>(int argIndex, CLBuffer<T> value) where T : struct
		{
			ErrorCode error;
			IntPtr bufferId = value.BufferId;
			error = (ErrorCode)CL.SetKernelArg(KernelId, argIndex, new IntPtr(sizeof(IntPtr)), new IntPtr(&bufferId));
			if (error != ErrorCode.Success) throw new System.InvalidOperationException("Error calling SetKernelArg");
		}
		public ulong KernelPreferredWorkGroupSizeMultiple
		{
			get
			{
				ErrorCode error;
				ulong ret = 0;
				error = (ErrorCode)CL.GetKernelWorkGroupInfo(KernelId, DeviceId, KernelWorkGroupInfo.KernelPreferredWorkGroupSizeMultiple, new IntPtr(sizeof(IntPtr)), ref ret, (IntPtr*)IntPtr.Zero.ToPointer());
				if (error != ErrorCode.Success) throw new System.InvalidOperationException("Error calling GetKernelWorkGroupInfo");
				return ret;
			}
		}
	}
}
