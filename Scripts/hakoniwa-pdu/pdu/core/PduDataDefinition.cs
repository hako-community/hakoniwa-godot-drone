using System;
using System.Collections.Generic;
using hakoniwa.pdu.interfaces;

namespace hakoniwa.pdu.core
{
    public class PduDataDefinition: IPduDataDefinition
    {
        public int TotalSize { get; set; }
        private Dictionary<string, IPduFieldDefinition> fieldDefinitions;

        public PduDataDefinition(Dictionary<string, IPduFieldDefinition> pdu_field_definitions)
        {
            this.fieldDefinitions = pdu_field_definitions;
        }

        public IPduFieldDefinition GetFieldDefinition(string fieldName)
        {
            if (fieldDefinitions.ContainsKey(fieldName))
            {
                return fieldDefinitions[fieldName];
            }
            return null;
        }
        public Dictionary<string, IPduFieldDefinition> Get()
        {
            return fieldDefinitions;
        }

    }
    public class PduArrayFieldDefinition: IPduArrayFieldDefinition
    {
        public int ArrayLen { get; set; }
    }

    
    public class PduFieldDefinition : IPduFieldDefinition
    {
        public FieldType Type { get; set; }
        public bool IsPrimitive { get; set; }
        public string MemberName { get; set; }
        // format: "<package_name>/<type_name>"
        public string DataTypeName { get; set; }
        public int ByteMemberOffset { get; set; }
        public int ByteMemberDataTypeSize { get; set; }
        public IPduArrayFieldDefinition ArrayInfo { get; set; }

        public string GetPackageName()
        {
            var parts = DataTypeName.Split("/");
            if (parts.Length != 2)
            {
                throw new InvalidOperationException($"DataTypeNameの形式が正しくありません: {DataTypeName}");
            }
            return parts[0];
        }

        public string GetTypeName()
        {
            var parts = DataTypeName.Split("/");
            if (parts.Length != 2)
            {
                throw new InvalidOperationException($"DataTypeNameの形式が正しくありません: {DataTypeName}");
            }
            return parts[1];
        }
    }
    public class HakoPduMetaDataType
    {
        public static readonly int PduMetaDataSize = 24;
        public static readonly uint PduMetaDataMagicNo = 0x12345678;
        public static readonly uint PduMetaDataVersion = 1;
        public uint magicno { get; set; }
        public uint version { get; set; }
        public uint base_off { get; set; }
        public uint heap_off { get; set; }
        public uint total_size { get; set; }
        public HakoPduMetaDataType(byte[] raw_data)
        {
            magicno = BitConverter.ToUInt32(raw_data, 0);
            version = BitConverter.ToUInt32(raw_data, 4);
            base_off = BitConverter.ToUInt32(raw_data, 8);
            heap_off = BitConverter.ToUInt32(raw_data, 12);
            total_size = BitConverter.ToUInt32(raw_data, 16);
        }
        public HakoPduMetaDataType(uint base_size)
        {
            magicno = PduMetaDataMagicNo;
            version = PduMetaDataVersion;
            base_off = (uint)PduMetaDataSize;
            heap_off = (uint)PduMetaDataSize + base_size;
            total_size = 0;
        }
        public void SetMetaDataToBuffer(byte[] buffer)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(magicno), 0, buffer, 0, sizeof(uint));
            Buffer.BlockCopy(BitConverter.GetBytes(version), 0, buffer, 4, sizeof(uint));
            Buffer.BlockCopy(BitConverter.GetBytes(base_off), 0, buffer, 8, sizeof(uint));
            Buffer.BlockCopy(BitConverter.GetBytes(heap_off), 0, buffer, 12, sizeof(uint));
            Buffer.BlockCopy(BitConverter.GetBytes(total_size), 0, buffer, 16, sizeof(uint));
        }
    }
}
