namespace hakoniwa.pdu.interfaces
{
    public interface IPduOperation
    {
        // Set data for a single value or array
        void SetData<T>(string field_name, T value);
        void SetData<T>(string field_name, T[] value);
        void SetData<T>(string field_name, int off, T value);

        // Get data as single value or array
        T GetData<T>(string field_name);
        T[] GetDataArray<T>(string field_name);
    }
}
