using System;
using hakoniwa.pdu.interfaces;

public class NamedPdu: INamedPdu
{
    private string robotName;
    private IPdu pdu;
    public NamedPdu(string robot_name, IPdu _pdu)
    {
        this.robotName = robot_name;
        this.pdu = _pdu;
    }
    public string RobtName => robotName;
    public IPdu Pdu => pdu;
    public string Name => pdu.Name;
    public string TypeName => pdu.TypeName;
    public string PackageName => pdu.PackageName;

    public T GetData<T>(string field_name)
    {
        return pdu.GetData<T>(field_name);
    }

    public T[] GetDataArray<T>(string field_name)
    {
        return pdu.GetDataArray<T>(field_name);
    }

    public void SetData<T>(string field_name, T value)
    {
        pdu.SetData<T>(field_name, value);
    }

    public void SetData<T>(string field_name, T[] value)
    {
        pdu.SetData<T>(field_name, value);
    }

    public void SetData<T>(string field_name, int off, T value)
    {
        pdu.SetData<T>(field_name, value);
    }
}
