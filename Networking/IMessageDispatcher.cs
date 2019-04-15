using System;

namespace Networking
{
	public interface IMessageDispatcher
	{
		void OnMessage(UserToken user, ArraySegment<byte> buffer);
	}
}
