
namespace ChickenIngot.Networking
{
	public struct Const<T>
	{
		public T Value { get; private set; }

		public Const(T value)
			: this()
		{
			Value = value;
		}
	}
}
