using System;

namespace Anz.Networking
{
	public interface IMessageDispatcher
	{
		void OnMessage(UserToken user, ArraySegment<byte> buffer);
	}
}
