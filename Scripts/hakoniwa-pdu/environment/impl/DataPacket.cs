using System;
using System.Text;
using hakoniwa.environment.interfaces;

namespace hakoniwa.environment.impl
{
    public class DataPacket: IDataPacket
    {
        public string RobotName { get; set; }
        public int ChannelId { get; set; }
        public byte[] BodyData { get; set; }
        public int GetChannelId()
        {
            return ChannelId;
        }

        public byte[] GetPduData()
        {
            return BodyData;
        }

        public string GetRobotName()
        {
            return RobotName;
        }

        /// <summary>
        /// データパケットの構造
        /// - 先頭4バイト: 後続するデータの全体長 (int32)
        /// - 次の4バイト: ヘッダデータの長さ (int32)
        /// - ヘッダデータ構造:
        ///     1. ロボット名の長さ (uint32)
        ///     2. ロボット名 (可変長の文字列、UTF-8エンコード)
        ///     3. チャネルID (int32)
        /// - ボディ部: 残りのデータはプロトコルでの解釈は行わず、バイナリデータとして保持
        /// </summary>
        /// <param name="data">解析するデータパケット</param>
        /// <returns>解析結果を保持する ParsedData オブジェクト</returns>
        /// <exception cref="ArgumentException">データの長さが不足している場合</exception>
        public static IDataPacket Decode(byte[] data)
        {
            if (data.Length < 12)
                throw new ArgumentException("データが短すぎます。");

            int currentIndex = 0;

            // ヘッダーの長さを取得
            int headerLength = BitConverter.ToInt32(data, currentIndex);
            currentIndex += 4;

            if (data.Length < 4 + headerLength)
                throw new ArgumentException("指定されたヘッダー長が不正です。");

            // ロボット名の長さを取得
            int robotNameLength = BitConverter.ToInt32(data, currentIndex);
            currentIndex += 4;

            // ロボット名を取得
            string robotName = Encoding.UTF8.GetString(data, currentIndex, robotNameLength);
            currentIndex += robotNameLength;

            // チャネルIDを取得
            int channelId = BitConverter.ToInt32(data, currentIndex);
            currentIndex += 4;

            // ボディデータの開始位置と長さを計算
            int bodyLength = data.Length - currentIndex;
            byte[] bodyData = new byte[bodyLength];
            Array.Copy(data, currentIndex, bodyData, 0, bodyLength);

            return new DataPacket
            {
                RobotName = robotName,
                ChannelId = channelId,
                BodyData = bodyData
            };
        }


        /// <summary>
        /// DataPacket オブジェクトからバイト配列にエンコードします。
        /// </summary>
        public byte[] Encode()
        {
            byte[] robotNameBytes = Encoding.UTF8.GetBytes(GetRobotName());
            int robotNameLength = robotNameBytes.Length;

            int headerLength = 
                4                                   // robot name length
                + robotNameLength                   // robot name
                + 4                                 // channel_id
                + (GetPduData()?.Length ?? 0);      //body length
            int totalLength = 4 + headerLength;

            byte[] data = new byte[totalLength];
            int currentIndex = 0;

            //header length
            BitConverter.GetBytes(headerLength).CopyTo(data, currentIndex);
            currentIndex += 4;
            //header
            {
                // robot name length
                BitConverter.GetBytes(robotNameLength).CopyTo(data, currentIndex);
                currentIndex += 4;

                // robot name
                robotNameBytes.CopyTo(data, currentIndex);
                currentIndex += robotNameLength;

                // channel id
                BitConverter.GetBytes(GetChannelId()).CopyTo(data, currentIndex);
                currentIndex += 4;
            }
            // body
            {
                GetPduData()?.CopyTo(data, currentIndex);
            }

            return data;
        }

    }
}

