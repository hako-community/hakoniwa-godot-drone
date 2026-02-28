using System;
using hakoniwa.pdu.interfaces;

namespace hakoniwa.pdu.core
{
    public class PduDecoder
    {
        private PduDataDefinitionLoader loader;
        public PduDecoder(PduDataDefinitionLoader ldr)
        {
            loader = ldr;
        }
        public Pdu Decode(string name, string packageName, string typeName, byte[] raw_data)
        {
            var definition = loader.LoadDefinition(packageName + "/" + typeName);
            Pdu dst = new Pdu(name, packageName, typeName, definition);
            var meta = new HakoPduMetaDataType(raw_data);
            ConvertFromStruct(dst, meta, HakoPduMetaDataType.PduMetaDataSize, raw_data);
            return dst;
        }
        private void ConvertFromStruct(IPdu idst, HakoPduMetaDataType meta, int base_off, byte[] src_buffer)
        {
            if (idst is Pdu pdu) {
                Pdu dst = idst as Pdu;
                foreach (var (fieldName, elm) in dst.GetPduDefinition().Get())
                {
                    if (elm.IsPrimitive)
                    {
                        //primitive
                        if (elm.Type == FieldType.FixedArray)
                        {
                            SetEmptyPrimitiveArray(dst, elm, elm.ArrayInfo.ArrayLen);
                            ConvertFromPrimtiveArray(dst, elm, base_off, elm.ByteMemberOffset, elm.ArrayInfo.ArrayLen, src_buffer);
                        }
                        else if (elm.Type == FieldType.VariableArray)
                        {
                            int array_size = BitConverter.ToInt32(src_buffer, base_off + elm.ByteMemberOffset);
                            //Console.WriteLine($"DEC {fieldName}:  {array_size}");
                            if (array_size == 0)
                            {
                                SetEmptyPrimitiveArray(dst, elm, 1);
                            }
                            else
                            {
                                SetEmptyPrimitiveArray(dst, elm, array_size);
                                int offset_from_heap = BitConverter.ToInt32(src_buffer, base_off + elm.ByteMemberOffset + 4);
                                ConvertFromPrimtiveArray(dst, elm, (int)meta.heap_off, offset_from_heap, array_size, src_buffer);
                            }
                        }
                        else
                        {
                            ConvertFromPrimtive(dst, elm, base_off, elm.ByteMemberOffset, src_buffer);
                        }
                    }
                    else
                    {
                        //struct
                        if (elm.Type == FieldType.FixedArray)
                        {
                            ConvertFromStructArray(dst, meta, elm, base_off, elm.ByteMemberOffset, elm.ArrayInfo.ArrayLen, src_buffer);
                        }
                        else if (elm.Type == FieldType.VariableArray)
                        {
                            int array_size = BitConverter.ToInt32(src_buffer, base_off + elm.ByteMemberOffset);
                            int offset_from_heap = BitConverter.ToInt32(src_buffer, base_off + elm.ByteMemberOffset + 4);
                            ConvertFromStructArray(dst, meta, elm, (int)meta.heap_off, offset_from_heap, array_size, src_buffer);
                        }
                        else
                        {
                            PduDataDefinition def = loader.LoadDefinition(elm.DataTypeName);
                            Pdu child_dst = new Pdu(elm.MemberName, elm.GetPackageName(), elm.GetTypeName(), def);
                            ConvertFromStruct(child_dst, meta, base_off + elm.ByteMemberOffset, src_buffer);
                            dst.SetData<IPdu>(elm.MemberName, child_dst);
                        }
                    }
                }
            }
            else
            {
                throw new InvalidCastException("IPdu オブジェクトを Pdu にキャストできませんでした。");
            }
        }
        private void ConvertFromStructArray(IPdu dst, HakoPduMetaDataType meta, IPduFieldDefinition elm, int base_off, int elm_off, int array_size, byte[] src_buffer)
        {
            PduDataDefinition def = loader.LoadDefinition(elm.DataTypeName);
            Pdu[] child_pdus = new Pdu[array_size];
            for (int i = 0; i < array_size; i++)
            {
                //Console.WriteLine($"DEC struct array: {i} : off: {base_off + elm_off} size: {elm.ByteMemberDataTypeSize}");
                child_pdus[i] = new Pdu(elm.MemberName, elm.GetPackageName(), elm.GetTypeName(), def);
                ConvertFromStruct(child_pdus[i], meta, (base_off + elm_off) + (i * elm.ByteMemberDataTypeSize), src_buffer);
            }
            dst.SetData(elm.MemberName, child_pdus);
        }
        private void ConvertFromPrimtive(IPdu dst, IPduFieldDefinition elm, int base_off, int elm_off, byte[] src_buffer)
        {
            var off = base_off + elm_off;
            //Console.WriteLine($"DEC {elm.MemberName} off: {off}");
            switch (elm.DataTypeName)
            {
                case "int8":
                    dst.SetData(elm.MemberName, (sbyte)src_buffer[off]);
                    break;
                case "int16":
                    dst.SetData(elm.MemberName, BitConverter.ToInt16(src_buffer, off));
                    break;
                case "int32":
                    dst.SetData(elm.MemberName, BitConverter.ToInt32(src_buffer, off));
                    break;
                case "int64":
                    dst.SetData(elm.MemberName, BitConverter.ToInt64(src_buffer, off));
                    break;
                case "uint8":
                    dst.SetData(elm.MemberName, (byte)src_buffer[off]);
                    break;
                case "uint16":
                    dst.SetData(elm.MemberName, BitConverter.ToUInt16(src_buffer, off));
                    break;
                case "uint32":
                    dst.SetData(elm.MemberName, BitConverter.ToUInt32(src_buffer, off));
                    break;
                case "uint64":
                    dst.SetData(elm.MemberName, BitConverter.ToUInt64(src_buffer, off));
                    break;
                case "float32":
                    dst.SetData(elm.MemberName, BitConverter.ToSingle(src_buffer, off));
                    break;
                case "float64":
                    dst.SetData(elm.MemberName, BitConverter.ToDouble(src_buffer, off));
                    break;
                case "bool":
                    dst.SetData(elm.MemberName, BitConverter.ToBoolean(src_buffer, off));
                    break;
                case "string":
                    // オフセット `off` 以降で null 終端文字 (`\0`) を探す
                    int nullIndex = Array.IndexOf(src_buffer, (byte)0, off);
                    if (nullIndex < 0)
                    {
                        throw new System.Exception($"Invalid string length detected for {elm.MemberName}");
                    }

                    // `\0` が見つからない場合、全体の長さを使用
                    int length = (nullIndex - off);
                    var bytes = new byte[length];
                    Buffer.BlockCopy(src_buffer, off, bytes, 0, length);

                    string decodedString = System.Text.Encoding.ASCII.GetString(bytes);
                    dst.SetData(elm.MemberName, decodedString);

                    //GD.Print($"len={length} {elm.MemberName} : string {decodedString}");
                    break;
                default:
                    throw new InvalidCastException("Error: Can not found ptype: " + elm.MemberName);
            }
        }
        private static void SetEmptyPrimitiveArray(IPdu dst, IPduFieldDefinition elm, int array_size)
        {
            switch (elm.DataTypeName)
            {
                case "int8":
                    dst.SetData<SByte>(elm.MemberName, new SByte[array_size]);
                    break;
                case "int16":
                    dst.SetData<Int16>(elm.MemberName, new Int16[array_size]);
                    break;
                case "int32":
                    dst.SetData<Int32>(elm.MemberName, new Int32[array_size]);
                    break;
                case "int64":
                    dst.SetData<Int64>(elm.MemberName, new Int64[array_size]);
                    break;
                case "uint8":
                    dst.SetData<Byte>(elm.MemberName, new Byte[array_size]);
                    break;
                case "uint16":
                    dst.SetData<UInt16>(elm.MemberName, new UInt16[array_size]);
                    break;
                case "uint32":
                    dst.SetData<UInt32>(elm.MemberName, new UInt32[array_size]);
                    break;
                case "uint64":
                    dst.SetData<UInt64>(elm.MemberName, new UInt64[array_size]);
                    break;
                case "float32":
                    dst.SetData<float>(elm.MemberName, new float[array_size]);
                    break;
                case "float64":
                    dst.SetData<double>(elm.MemberName, new double[array_size]);
                    break;
                case "bool":
                    dst.SetData<bool>(elm.MemberName, new bool[array_size]);
                    break;
                case "string":
                    var strs = new string[array_size];
                    for (int i = 0; i < array_size; i++)
                    {
                        strs[i] = "";
                    }
                    dst.SetData<string>(elm.MemberName, strs);
                    break;
                default:
                    throw new InvalidCastException("Error: Can not found ptype: " + elm.DataTypeName);
            }
        }
        private static void ConvertFromPrimtiveArray(IPdu dst, IPduFieldDefinition elm, int base_off, int elm_off, int array_size, byte[] src_buffer)
        {
            int roff = base_off + elm_off;
            //Console.WriteLine($"DEC array {elm.MemberName} roff: {roff} array_size: {array_size}");
            for (int i = 0; i < array_size; i++)
            {
                //SimpleLogger.Get().Log(Level.INFO, "field=" + elm.field_name);
                //SimpleLogger.Get().Log(Level.INFO, "type=" + elm.type_name);
                var off = (roff + i * elm.ByteMemberDataTypeSize);
                switch (elm.DataTypeName)
                {
                    case "int8":
                        dst.SetData(elm.MemberName, i, (sbyte)src_buffer[off]);
                        break;
                    case "int16":
                        dst.SetData(elm.MemberName, i, BitConverter.ToInt16(src_buffer, off));
                        break;
                    case "int32":
                        dst.SetData(elm.MemberName, i, BitConverter.ToInt32(src_buffer, off));
                        break;
                    case "int64":
                        dst.SetData(elm.MemberName, i, BitConverter.ToInt64(src_buffer, off));
                        break;
                    case "uint8":
                        dst.SetData(elm.MemberName, i, (byte)src_buffer[off]);
                        break;
                    case "uint16":
                        dst.SetData(elm.MemberName, i, BitConverter.ToUInt16(src_buffer, off));
                        break;
                    case "uint32":
                        dst.SetData(elm.MemberName, i, BitConverter.ToUInt32(src_buffer, off));
                        break;
                    case "uint64":
                        dst.SetData(elm.MemberName, i, BitConverter.ToUInt64(src_buffer, off));
                        break;
                    case "float32":
                        dst.SetData(elm.MemberName, i, BitConverter.ToSingle(src_buffer, off));
                        break;
                    case "float64":
                        dst.SetData(elm.MemberName, i, BitConverter.ToDouble(src_buffer, off));
                        break;
                    case "bool":
                        dst.SetData(elm.MemberName, i, BitConverter.ToBoolean(src_buffer, off));
                        break;
                    case "string":
                        var bytes = new byte[elm.ByteMemberDataTypeSize];
                        Buffer.BlockCopy(src_buffer, off, bytes, 0, bytes.Length);
                        dst.SetData(elm.MemberName, i,
                            System.Text.Encoding.ASCII.GetString(bytes));
                        break;
                    default:
                        throw new InvalidCastException("Error: Can not found ptype: " + elm.DataTypeName);
                }
            }
        }
    }
}
