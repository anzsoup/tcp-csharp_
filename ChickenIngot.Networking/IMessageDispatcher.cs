using System;

namespace ChickenIngot.Networking
{
	public interface IMessageDispatcher
	{
		void OnMessage(UserToken user, ArraySegment<byte> buffer);
	}
}
