using System;
using System.Collections.Generic;
using System.IO;
using hakoniwa.environment.interfaces;
using hakoniwa.pdu.interfaces;

namespace hakoniwa.pdu.core
{
    public class PduDataDefinitionLoader
    {
        private Dictionary<string, PduDataDefinition> definitions = new Dictionary<string, PduDataDefinition>();
        private IFileLoader file_loader;
        private string config_path;
        private static readonly HashSet<string> PrimitiveTypes = new HashSet<string>
        { "int8", "int16", "int32", "int64", "uint8", "uint16", "uint32", "uint64", "float32", "float64", "bool", "string" };

        public PduDataDefinitionLoader(IFileLoader ldr, string _config_path)
        {
            this.config_path = _config_path;
            file_loader = ldr ?? throw new ArgumentNullException(nameof(ldr));
        }

        public PduDataDefinition LoadDefinition(string package_type_name)
        {
            if (definitions.ContainsKey(package_type_name))
            {
                return definitions[package_type_name];
            }
            string relative_path = "ros_types/offset/" + package_type_name;
            string normalizedPackageTypeName = relative_path.Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.Combine(config_path, normalizedPackageTypeName);
            string textContent = file_loader.LoadText(fullPath, ".offset");
            if (string.IsNullOrWhiteSpace(textContent))
            {
                throw new FileNotFoundException($"Could not load definition for {package_type_name}");
            }
            var definition = Parse(package_type_name, textContent);
            definitions[package_type_name] = definition;
            return definition;
        }

        private PduDataDefinition Parse(string package_type_name, string textContent)
        {
            string package_name = package_type_name.Split("/")[0];
            string[] lines = textContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            Dictionary<string, IPduFieldDefinition> fieldDefinitions = new Dictionary<string, IPduFieldDefinition>();
            int total_size = 0;

            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                string[] attr = line.Split(':');
                if (attr.Length < 6)
                    throw new FormatException($"Invalid format in line: {line}");

                var elm = CreateFieldDefinition(attr, package_name);
                fieldDefinitions[elm.MemberName] = elm;

                // 全体の PDU サイズの計算
                total_size = Math.Max(total_size, GetSize(elm, attr));
            }

            var definition = new PduDataDefinition(fieldDefinitions)
            {
                TotalSize = total_size
            };
            return definition;
        }

        private int GetSize(PduFieldDefinition elm, string[] attr)
        {
            switch (elm.Type)
            {
                case FieldType.Single:
                    return elm.ByteMemberOffset + elm.ByteMemberDataTypeSize;

                case FieldType.FixedArray:
                    return elm.ByteMemberOffset + (elm.ByteMemberDataTypeSize * elm.ArrayInfo.ArrayLen);

                case FieldType.VariableArray:
                    return elm.ByteMemberOffset + int.Parse(attr[6]);

                default:
                    throw new InvalidOperationException($"Unsupported type for field {elm.MemberName}");
            }
        }
        private int GetDataTypeSize(string[] attr)
        {
            return attr[0] != "array" ? int.Parse(attr[5]) : int.Parse(attr[5]) / int.Parse(attr[6]);
        }

        private PduFieldDefinition CreateFieldDefinition(string[] attr, string package_name)
        {
            PduFieldDefinition elm = new PduFieldDefinition
            {
                Type = attr[0] switch
                {
                    "array" => FieldType.FixedArray,
                    "varray" => FieldType.VariableArray,
                    _ => FieldType.Single
                },
                IsPrimitive = attr[1].Equals("primitive"),
                MemberName = attr[2],
                DataTypeName = attr[3].Contains("/") || IsPrimitive(attr[3]) ? attr[3] : package_name + "/" + attr[3],
                ByteMemberOffset = int.Parse(attr[4]),
                ByteMemberDataTypeSize = GetDataTypeSize(attr)
            };

            if (elm.Type != FieldType.Single)
            {
                elm.ArrayInfo = new PduArrayFieldDefinition
                {
                    ArrayLen = elm.Type == FieldType.FixedArray ? int.Parse(attr[6]) : -1
                };
            }

            return elm;
        }


        private static bool IsPrimitive(string type_name)
        {
            return PrimitiveTypes.Contains(type_name);
        }
    }
}
