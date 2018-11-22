using System.Collections.Generic;

namespace Anz.Networking
{
	public interface ILogicQueue
	{
		void Enqueue(Packet msg);

		Queue<Packet> GetAll();
	}
}
