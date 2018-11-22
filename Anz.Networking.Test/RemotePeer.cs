using System;

namespace Anz.Networking.Test
{
	class RemotePeer : IPeer
	{
		private readonly UserToken _token;
		private readonly Action<Packet> _onMessage;

		public RemotePeer(UserToken token, Action<Packet> onMessage)
		{
			_token = token;
			_token.SetPeer(this);
			_onMessage = onMessage;
		}

		public void Disconnect()
		{
			throw new NotImplementedException();
		}

		public void OnMessage(Packet msg)
		{
			_onMessage(msg);
		}

		public void OnRemoved()
		{
			throw new NotImplementedException();
		}

		public void Send(Packet msg)
		{
			_token.Send(msg);
		}
	}
}
