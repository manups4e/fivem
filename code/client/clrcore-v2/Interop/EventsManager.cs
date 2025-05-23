using CitizenFX.MsgPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;

using CitizenFX.Core.Native;

namespace CitizenFX.Core
{
	internal static class EventsManager
	{
		private static Dictionary<string, List<Tuple<MsgPackFunc, Binding>>> s_eventHandlers = new Dictionary<string, List<Tuple<MsgPackFunc, Binding>>>();


		[SecuritySafeCritical]
		internal static unsafe void IncomingEvent(string eventName, string sourceString, Binding origin, byte* argsSerialized, int serializedSize)
		{
			if (s_eventHandlers.TryGetValue(eventName, out var delegateList))
			{
				Remote remote = new Remote(origin, sourceString);

				CitizenFX.MsgPack.MsgPackDeserializer deserializer = new MsgPackDeserializer(argsSerialized, (ulong)serializedSize, origin == Binding.Remote ? sourceString : null);
				var restorePoint = deserializer.CreateRestorePoint();

				for (int i = 0; i < delegateList.Count; ++i)
				{
					var ev = delegateList[i];

					try
					{
						if ((ev.Item2 & origin) != 0)
						{
							var result = ev.Item1(remote, ref deserializer);
							if (result is bool b && !b)
								return;

							deserializer.Restore(restorePoint);
						}
					}
					catch (Exception ex)
					{
						deserializer.Restore(restorePoint);
						var args = deserializer.DeserializeAsObjectArray();
						Debug.WriteException(ex, ev.Item1, args, "event handler");
					}
				}
			}
		}

		#region Registration
		internal static void AddEventHandler(string eventName, MsgPackFunc del, Binding binding = Binding.Local)
		{
			if (!s_eventHandlers.TryGetValue(eventName, out var delegateList))
			{
				delegateList = new List<Tuple<MsgPackFunc, Binding>>();
				s_eventHandlers.Add(eventName, delegateList);

				CoreNatives.RegisterResourceAsEventHandler(eventName);
			}

			delegateList.Add(new Tuple<MsgPackFunc, Binding>(del, binding));
		}

		internal static void RemoveEventHandler(string eventName, Delegate del)
		{
			if (s_eventHandlers.TryGetValue(eventName, out var delegateList))
			{
				int index = delegateList.FindIndex(cur => cur.Item1.Equals(del));
				if (index != -1)
				{
					s_eventHandlers[eventName].RemoveAt(index);
				}
			}
		}
		#endregion
	}

	public class EventHandler : Dictionary<string, EventHandlerSet>
	{
		public new EventHandlerSet this[string key]
		{
			get
			{
				var lookupKey = key.ToLower();

				if (!TryGetValue(lookupKey, out var entry))
				{
					entry = new EventHandlerSet(key);
					base.Add(lookupKey, entry);
				}

				return entry;
			}
			set { /* ignore, this will enable += syntax */ }
		}

		public void Add(string key, MsgPackFunc value) => this[key].Add(value);

		/// <summary>
		/// Should only be called by <see cref="BaseScript.Enable"/> or any other code that guarantees that it is only called once
		/// </summary>
		internal void Enable()
		{
			foreach (var ev in this)
			{
				ev.Value.Enable();
			}
		}

		internal void Disable()
		{
			foreach (var ev in this)
			{
				ev.Value.Disable();
			}
		}
	}

	public class EventHandlerSet
	{
		private readonly string m_eventName;
		private readonly List<MsgPackFunc> m_handlers = new List<MsgPackFunc>();

		public EventHandlerSet(string eventName)
		{
			m_eventName = eventName;
		}

		~EventHandlerSet()
		{
			Disable();
		}

		/// <summary>
		/// Register an event handler
		/// </summary>
		/// <param name="deleg">delegate to call once triggered</param>
		/// <param name="binding">limit calls to certain sources, e.g.: server only, client only</param>
		public EventHandlerSet Add(MsgPackFunc deleg, Binding binding = Binding.Local)
		{
			m_handlers.Add(deleg);
			EventsManager.AddEventHandler(m_eventName, deleg, binding);
			return this;
		}

		/// <summary>
		/// Unregister an event handler
		/// </summary>
		/// <param name="deleg">delegate to remove</param>
		public EventHandlerSet Remove(Delegate deleg)
		{
			int index = m_handlers.FindIndex(cur => deleg.Equals(cur));
			if (index != -1)
			{
				m_handlers.RemoveAt(index);
				EventsManager.RemoveEventHandler(m_eventName, deleg);
			}
			return this;
		}

		/// <summary>
		/// Register an event handler
		/// </summary>
		/// <remarks>Will add it as <see cref="Binding.Local"/>, use <see cref="Add(MsgPackFunc, Binding)"/> to explicitly set the binding.</remarks>
		/// <param name="entry">this event handler set</param>
		/// <param name="deleg">delegate to register</param>
		/// <returns>itself</returns>
		public static EventHandlerSet operator +(EventHandlerSet entry, MsgPackFunc deleg) => entry.Add(deleg);

		/// <summary>
		/// Unregister an event handler
		/// </summary>
		/// <param name="entry">this event handler set</param>
		/// <param name="deleg">delegate to register</param>
		/// <returns>itself</returns>
		public static EventHandlerSet operator -(EventHandlerSet entry, MsgPackFunc deleg) => entry.Remove(deleg);

		/// <summary>
		/// Register an event handler
		/// </summary>
		/// <remarks>Will add it as <see cref="Binding.Local"/>, use <see cref="Add(MsgPackFunc, Binding)"/> to explicitly set the binding.</remarks>
		/// <param name="entry">this event handler set</param>
		/// <param name="deleg">delegate to register</param>
		/// <returns>itself</returns>
		public static EventHandlerSet operator +(EventHandlerSet entry, Delegate deleg) => entry.Add(MsgPackDeserializer.CreateDelegate(deleg));

		/// <summary>
		/// Unregister an event handler
		/// </summary>
		/// <param name="entry">this event handler set</param>
		/// <param name="deleg">delegate to register</param>
		/// <returns>itself</returns>
		public static EventHandlerSet operator -(EventHandlerSet entry, Delegate deleg) => entry.Remove(deleg);


		/// <summary>
		/// Should only be called by <see cref="EventHandler.Enable"/> or any other code that guarantees that it is only called once
		/// </summary>
		internal void Enable()
		{
			for (int i = 0; i < m_handlers.Count; ++i)
			{
				EventsManager.AddEventHandler(m_eventName, m_handlers[i]);
			}
		}

		internal void Disable()
		{
			for (int i = 0; i < m_handlers.Count; ++i)
			{
				EventsManager.RemoveEventHandler(m_eventName, m_handlers[i]);
			}
		}
	}
}
