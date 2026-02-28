using System;
using System.Collections.Generic;
using hakoniwa.pdu.interfaces;

namespace hakoniwa.pdu.core
{
    public class Pdu : IPdu
    {
        private Dictionary<string, object> fields = new Dictionary<string, object>();

        public string Name { get; }
        public string TypeName { get; }
        public string PackageName { get; }
        private readonly PduDataDefinition pdu_definition;

        private static readonly Dictionary<string, Type> RosToCSharpTypeMap = new Dictionary<string, Type>
        {
            { "int8", typeof(sbyte) },
            { "int16", typeof(short) },
            { "int32", typeof(int) },
            { "int64", typeof(long) },
            { "uint8", typeof(byte) },
            { "uint16", typeof(ushort) },
            { "uint32", typeof(uint) },
            { "uint64", typeof(ulong) },
            { "float32", typeof(float) },
            { "float64", typeof(double) },
            { "bool", typeof(bool) },
            { "string", typeof(string) }
        };

        public Pdu(string name, string packageName, string typeName, PduDataDefinition definition)
        {
            this.Name = name;
            this.TypeName = typeName;
            this.PackageName = packageName;
            this.pdu_definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }
        public string GetPackageTypeName()
        {
            return this.PackageName + "/" + this.TypeName;
        }
        public IPduDataDefinition GetPduDefinition()
        {
            return pdu_definition;
        }
        private IPduFieldDefinition GetFieldDefinitionOrThrow(string field_name)
        {
            var field = pdu_definition.GetFieldDefinition(field_name);
            if (field == null)
            {
                throw new KeyNotFoundException($"Field '{field_name}' does not exist in the PDU '{this.Name}' of type '{this.TypeName}'.");
            }
            return field;
        }

        public void SetData<T>(string field_name, T value)
        {
            var field = GetFieldDefinitionOrThrow(field_name);

            if (field.IsPrimitive)
            {
                // プリミティブ型の場合は、ROS型とC#型のマップを確認して型が一致するかをチェック
                if (RosToCSharpTypeMap.TryGetValue(field.DataTypeName, out Type expectedType))
                {
                    if (expectedType != typeof(T))
                    {
                        throw new InvalidCastException($"Field '{field_name}' expects data of type {expectedType.Name}, but {typeof(T).Name} was provided.");
                    }
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported primitive type '{field.DataTypeName}' for field '{field_name}'.");
                }
            }
            else
            {
                // 非プリミティブ型の場合、valueはIPduであることを確認する
                if (!(value is IPdu))
                {
                    throw new InvalidCastException($"Field '{field_name}' expects an IPdu type, but {typeof(T).Name} was provided.");
                }
            }

            fields[field_name] = value;
        }

        public void SetData<T>(string field_name, T[] value)
        {
            var field = GetFieldDefinitionOrThrow(field_name);
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value), $"Field '{field_name}' cannot be set to null.");
            }

            if (field.IsPrimitive)
            {
                if (RosToCSharpTypeMap.TryGetValue(field.DataTypeName, out Type expectedType))
                {
                    if (expectedType != typeof(T))
                    {
                        throw new InvalidCastException($"Field '{field_name}' expects an array of type {expectedType.Name}, but {typeof(T).Name} was provided.");
                    }
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported primitive type '{field.DataTypeName}' for field '{field_name}'.");
                }
            }
            else
            {
                if (!(value is IPdu[]))
                {
                    throw new InvalidCastException($"Field '{field_name}' expects an array of IPdu type, but {typeof(T).Name} was provided.");
                }
            }

            fields[field_name] = value.Clone();
        }

        public T GetData<T>(string field_name)
        {
            var field = GetFieldDefinitionOrThrow(field_name);
            if (fields[field_name] is T value)
            {
                return value;
            }
            throw new InvalidCastException($"Field '{field_name}' does not contain data of type {typeof(T)} in PDU '{this.Name}'.");
        }

        public T[] GetDataArray<T>(string field_name)
        {
            var field = GetFieldDefinitionOrThrow(field_name);
            if (fields[field_name] is T[] array)
            {
                return (T[])array.Clone();
            }
            throw new InvalidCastException($"Field '{field_name}' does not contain an array of type {typeof(T)} in PDU '{this.Name}'.");
        }

        public void SetData<T>(string field_name, int off, T value)
        {
            if (!fields.ContainsKey(field_name))
            {
                throw new KeyNotFoundException($"Field '{field_name}' does not exist in the PDU.");
            }

            if (fields[field_name] == null || !(fields[field_name] is T[] array))
            {
                array = new T[off + 1];
                fields[field_name] = array;
            }

            if (off >= array.Length)
            {
                Array.Resize(ref array, off + 1);
                fields[field_name] = array;
            }
            array[off] = value;
        }
    }
}
