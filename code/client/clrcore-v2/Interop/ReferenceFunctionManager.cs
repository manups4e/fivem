using CitizenFX.MsgPack;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;

namespace CitizenFX.Core
{
	internal static class ReferenceFunctionManager
	{
		internal class Function
		{
			public MsgPackFunc m_method;
			public readonly byte[] m_refId;
			public int m_refCount;

			public Function(MsgPackFunc method, byte[] id)
			{
				m_method = method;
				m_refId = id;
				m_refCount = 0;
			}
		}

		private static Dictionary<int, Function> s_references = new Dictionary<int, Function>();

		static ReferenceFunctionManager()
		{
			MsgPackReferenceRegistrar.CreateFunc = Create;
		}

		/// <summary>
		/// Register a delegate to other runtimes and/or our host (reference function)
		/// </summary>
		/// <param name="method">Delagate to register to the external world</param>
		/// <returns>( internalReferenceId, externalReferenceId )</returns>
		/// <remarks>Don't alter the returned value</remarks>
		[SecuritySafeCritical]
		internal static KeyValuePair<int, byte[]> Create(MsgPackFunc method)
		{
			// TODO: change return type to `ValueTuple` once clients support those

			int id = method.Method.GetHashCode();

			// keep incrementing until we find a free spot
			while (s_references.ContainsKey(id))
				unchecked { ++id; }

			byte[] refId = ScriptInterface.CanonicalizeRef(id);
			s_references[id] = new Function(method, refId);

			return new KeyValuePair<int, byte[]>(id, refId);
		}

		internal static int IncrementReference(int reference)
		{
			if (s_references.TryGetValue(reference, out var funcRef))
			{
				Interlocked.Increment(ref funcRef.m_refCount);
				return reference;
			}

			return -1;
		}

		internal static void DecrementReference(int reference)
		{
			if (s_references.TryGetValue(reference, out var funcRef)
				&& Interlocked.Decrement(ref funcRef.m_refCount) <= 0)
			{
				s_references.Remove(reference);
			}
		}

		/// <summary>
		/// Remove reference function by id
		/// </summary>
		/// <param name="referenceId">Internal reference id of the reference to remove</param>
		internal static void Remove(int referenceId)
		{			
			s_references.Remove(referenceId);
		}

		/// <summary>
		/// Remove all reference functions that are targeting a specific object
		/// </summary>
		/// <remarks>Slow, may need to be replaced</remarks>
		/// <param name="target"></param>
		internal static void RemoveAllWithTarget(object target)
		{
			foreach (var entry in s_references)
			{
				if (entry.Value.m_method.Target == target)
				{
					s_references.Remove(entry.Key);
				}
			}
		}

		/// <summary>
		/// Set reference function to another delegate
		/// </summary>
		/// <param name="referenceId">Reference id of the reference to remove</param>
		/// <param name="newFunc">New delegate/method to set the reference function to</param>
		/// <returns><see langword="true"/> if found and changed, <see langword="false"/> otherwise</returns>
		internal static bool SetDelegate(int referenceId, MsgPackFunc newFunc)
		{
			if (s_references.TryGetValue(referenceId, out var refFunc))
			{
				refFunc.m_method = newFunc;
				return true;
			}

			return false;
		}

		internal static int CreateCommand(string command, MsgPackFunc method, bool isRestricted)
		{
			var registration = Create(method);
			Native.CoreNatives.RegisterCommand(command, new Native.InFunc(registration.Value), isRestricted);

			return registration.Key;
		}

		[SecurityCritical]
		internal unsafe static void IncomingCall(int refIndex, byte* argsSerialized, uint argsSize, out byte[] retval)
		{
			try
			{
				retval = Invoke(refIndex, argsSerialized, argsSize);
			}
			catch (Exception e)
			{

				Debug.PrintError(e.InnerException ?? e, "reference call");
				retval = null;
			}
		}

		[SecurityCritical]
		internal unsafe static byte[] Invoke(int reference, byte* arguments, uint argsSize)
		{
			if (s_references.TryGetValue(reference, out var funcRef))
			{
				var deserializer = new MsgPackDeserializer(arguments, argsSize, null);
				var restorePoint = deserializer.CreateRestorePoint();

				object result = null;

				try
				{
					// there's no remote invocation support through here
					result = funcRef.m_method(default, ref deserializer);
				}
				catch (Exception ex)
				{
					deserializer.Restore(restorePoint);
					var args = deserializer.DeserializeAsObjectArray();
					Debug.WriteException(ex, funcRef.m_method, args, "reference function");
				}

				if (result is Coroutine coroutine)
				{
					if (coroutine.IsCompleted)
					{
						if (coroutine.Exception != null)
						{
							Debug.Write(coroutine.Exception);
						}

						return MsgPackSerializer.SerializeToByteArray(new[] { coroutine.GetResultNonThrowing(), coroutine.Exception?.ToString() });
					}
					else
					{
						var returnDictionary = new Dictionary<string, object>()
						{
							{ "__cfx_async_retval", new Action<Callback>(asyncResult =>
								coroutine.ContinueWith(() =>
								{
									if (coroutine.Exception != null)
									{
										Debug.Write(coroutine.Exception);
									}

									asyncResult(coroutine.GetResultNonThrowing(), coroutine.Exception?.ToString());
								}))
							}
						};

						return MsgPackSerializer.SerializeToByteArray(new object[] { returnDictionary });
					}
				}

				return MsgPackSerializer.SerializeToByteArray(new[] { result });
			}
			else
			{
				Debug.WriteLine($"No such reference for {reference}.");
			}

			return new byte[] { 0xC0 }; // return nil
		}
	}
}
