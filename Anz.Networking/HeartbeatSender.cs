using System.Threading;

namespace Anz.Networking
{
	public class HeartbeatSender
	{
		private UserToken _server;
		private Timer _timerHeartbeat;
		private uint _interval;
		private float _elapsedTime;


		public HeartbeatSender(UserToken server, uint interval)
		{
			_server = server;
			_interval = interval;
			_timerHeartbeat = new Timer(this.OnTimer, null, Timeout.Infinite, _interval * 1000);
		}


		private void OnTimer(object state)
		{
			Send();
		}


		void Send()
		{
			Packet msg = Packet.Create((short)UserToken.SYS_UPDATE_HEARTBEAT);
			_server.Send(msg);
		}


		public void Update(float time)
		{
			_elapsedTime += time;
			if (_elapsedTime < _interval)
			{
				return;
			}

			_elapsedTime = 0.0f;
			Send();
		}


		public void Stop()
		{
			_elapsedTime = 0;
			_timerHeartbeat.Change(Timeout.Infinite, Timeout.Infinite);
		}


		public void Play()
		{
			_elapsedTime = 0;
			_timerHeartbeat.Change(0, _interval * 1000);
		}
	}
}
