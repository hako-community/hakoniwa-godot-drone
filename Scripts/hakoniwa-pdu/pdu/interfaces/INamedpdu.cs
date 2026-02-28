namespace hakoniwa.pdu.interfaces
{
    public interface INamedPdu: IPdu
    {
        string RobtName { get; }
        IPdu Pdu { get; }
    }
}
