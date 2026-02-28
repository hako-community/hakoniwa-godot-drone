namespace hakoniwa.pdu.interfaces
{
    public interface IPdu : IPduOperation
    {
        // PDU の基本情報
        string Name { get; }
        string TypeName { get; }
        string PackageName { get; }
    }
}
