using System.Collections.Generic;

namespace ChickenIngot.Networking
{
	public interface ILogicQueue
	{
		void Enqueue(Packet msg);

		Queue<Packet> GetAll();
	}
}
