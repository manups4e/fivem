using CitizenFX.Core;
using CitizenFX.MsgPack;

namespace CitizenFX.Shared
{
#if IS_FXSERVER
	public abstract class Player
	{
		// TODO: update to CString, needs a uint.Parse(CString) like option first
		internal string m_handle;

		internal Player()
		{
		}

		public Player(ulong handle)
		{
			m_handle = handle.ToString();
		}

		/// <summary>
		/// Gets the handle of this player
		/// </summary>
		public int Handle => int.Parse(m_handle);
#else
	[MsgPackSerializable(Layout.Indexed)]
	public abstract class Player : Core.Native.Input.Primitive
	{
		public Player(ulong handle) : base(handle)
		{
		}

		/// <summary>
		/// Gets the handle of this player
		/// </summary>
		[Index(0)] public int Handle => (int)m_nativeValue;
#endif
		public override string ToString() => $"{nameof(Player)}({Handle})";

		/// <summary>
		/// Gets the name of this player
		/// </summary>
		public abstract string Name { get; }

		/// <summary>
		/// Access the <see cref="StateBag"/> of this player
		/// </summary>
		public abstract StateBag State { get; }

		/// <summary>
		/// Gets the <see cref="IPed"/> this player is controling, use Ped.Character on Ped for non-shared access.
		/// </summary>
		public abstract IPed GetCharacter();
	}
}
