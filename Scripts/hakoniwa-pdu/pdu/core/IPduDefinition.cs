using System;
using System.Collections.Generic;

namespace hakoniwa.pdu.core
{
    public interface IPduDataDefinition
    {
        IPduFieldDefinition GetFieldDefinition(string fieldName);
        Dictionary<string, IPduFieldDefinition> Get();
    }

    public interface IPduArrayFieldDefinition
    {
        int ArrayLen { get; set; }
    }

    public interface IPduFieldDefinition
    {
        FieldType Type { get; set; }
        bool IsPrimitive { get; set; }
        string MemberName { get; set; }
        string DataTypeName { get; set; } 
        int ByteMemberOffset { get; set; }
        int ByteMemberDataTypeSize { get; set; }
        IPduArrayFieldDefinition ArrayInfo { get; set; }

        string GetPackageName();
        string GetTypeName();
    }

    public enum FieldType
    {
        Single,       // 配列ではない単一のメンバ
        FixedArray,   // 固定長の配列
        VariableArray // 可変長の配列
    }
}
