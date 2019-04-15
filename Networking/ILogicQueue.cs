using System.Collections.Generic;

namespace Networking
{
	public interface ILogicQueue
	{
		void Enqueue(Packet msg);

		Queue<Packet> GetAll();
	}
}
