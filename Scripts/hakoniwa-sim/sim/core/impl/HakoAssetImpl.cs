using System.Collections;
using static hakoniwa.sim.core.impl.HakoCppWrapper;
using System;
using System.Collections.Generic;
using hakoniwa.environment.interfaces;
using hakoniwa.environment.impl;
using hakoniwa.pdu.interfaces;
using hakoniwa.pdu.core;
using System.Threading.Tasks;
//#if UNITY_EDITOR
using Godot;
//#endif

namespace hakoniwa.sim.core.impl
{
    public class HakoAssetImpl : IHakoAsset, IHakoCommand
    {
        private string my_asset_name;
        private string pduConfigPath;
        private string customJsonFilePath;
        private long asset_time_usec = 0;
        private long delta_time_usec;
        private bool isReady = false;
        private List<IHakoObject> hakoObectList;
        private IPduManager pduManager;
        private IEnvironmentService hako_service;
        private IHakoCommunicationService hako_pdu;


        public HakoAssetImpl(string asset_name, long d_time_usec, string pdu_config_path, string custom_json_file_path)
        {
            my_asset_name = asset_name;
            delta_time_usec = d_time_usec;
            pduConfigPath = pdu_config_path;
            customJsonFilePath = custom_json_file_path;
        }
        /// <summary>
        /// ★ 2026-10-18: 箱庭（共有メモリのマスタ）が居るか（courses の 2026-10-04 の対策と同じ）。
        /// 箱庭が居ないまま `asset_init` → `asset_register_polling` を呼ぶと、shakoc の中で 0xC0000005 で**落ちる**
        /// （`asset_init` は「Not found hako-master」でも true を返す）。マスタは core_mmap_path に mmap-0xff.bin を作る。
        /// 設定（HAKO_CONFIG_PATH）が読めなければ確かめようが無いので、従来どおり呼ぶ（true）。
        /// </summary>
        public static bool HakoniwaMasterExists(bool verbose = true)
        {
            try
            {
                string cfg = System.Environment.GetEnvironmentVariable("HAKO_CONFIG_PATH");
                if (string.IsNullOrEmpty(cfg) || !System.IO.File.Exists(cfg)) return true;
                string text = System.IO.File.ReadAllText(cfg);
                var m = System.Text.RegularExpressions.Regex.Match(text, @"""core_mmap_path""\s*:\s*""([^""]*)""");
                if (!m.Success) return true;
                string dir = m.Groups[1].Value.Replace("\\\\", "\\");
                string path = System.IO.Path.Combine(dir, "mmap-0xff.bin");
                bool exists = System.IO.File.Exists(path);
                if (!exists && verbose) GD.Print($"HakoAssetImpl: 箱庭が居ない（{dir} に mmap-0xff.bin が無い）");
                if (!exists) return false;
                // ★ 2026-10-18: プラントが強制終了されると mmap-0xff.bin が残る。そのときは登録まで成功してしまい、
                // 来ない START をずっと待つ（オフラインにもならない）。hako-cmd の probe_runtime と同じく、
                // ファイルに書かれたマスタの PID が生きているかで確かめる。
                int pid = ReadMasterPid(path);
                if (pid > 0 && !IsProcessAlive(pid))
                {
                    if (verbose) GD.Print($"HakoAssetImpl: 箱庭が居ない（{path} は前のプラントの残り。マスタ pid={pid} は動いていない）");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                if (verbose) GD.Print($"HakoAssetImpl: 箱庭の有無を確かめられない（{ex.Message}）→ 従来どおり繋ぎにいく");
                return true;
            }
        }

        // mmap-0xff.bin の頭（SharedMemoryMetaDataType: magic・version・shm_id・sem_id・data_size の 24 バイト）の次が
        // HakoMasterDataType で、その先頭が master_pid（4 バイト）。形が違えば確かめられないので 0 を返す。
        private const uint HakoShmMagic = 0x48414B4F;
        private const uint HakoShmLayoutVersion = 2;
        private const int MasterPidOffset = 24;

        private static int ReadMasterPid(string path)
        {
            try
            {
                using var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);
                var buf = new byte[MasterPidOffset + 4];
                if (fs.Read(buf, 0, buf.Length) != buf.Length) return 0;
                if (BitConverter.ToUInt32(buf, 0) != HakoShmMagic || BitConverter.ToUInt32(buf, 4) != HakoShmLayoutVersion) return 0;
                return BitConverter.ToInt32(buf, MasterPidOffset);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static bool IsProcessAlive(int pid)
        {
            try
            {
                using var p = System.Diagnostics.Process.GetProcessById(pid);
                return !p.HasExited;
            }
            catch (ArgumentException)
            {
                return false;   // その PID のプロセスは無い
            }
            catch (Exception)
            {
                return true;    // 権限などで確かめられない（hako-cmd も生きているとみなす）
            }
        }

        public bool Initialize(List<IHakoObject> list)
        {
            hakoObectList = list;
            if (!HakoniwaMasterExists())
            {
                isReady = false;
                return false;
            }
            isReady = HakoCppWrapper.asset_init();
            if (isReady)
            {
                hako_service = HakoEnvironmentServiceFactory.Create(my_asset_name);
                if (hako_service.GetCommunication() is IHakoCommunicationService)
                {
                    hako_pdu = (IHakoCommunicationService)hako_service.GetCommunication();
                }
                else
                {
                    throw new Exception("Can not cast IHakoCommunicationService");
                }
                pduManager = new PduManager(hako_service, pduConfigPath, customJsonFilePath);
            }
            return isReady;
        }

        private void PollEvent()
        {
            HakoSimAssetEvent ev = HakoCppWrapper.asset_get_event(my_asset_name);
            switch (ev)
            {
                case HakoSimAssetEvent.HakoSimAssetEvent_Start:
                    StartCallback();
                    break;
                case HakoSimAssetEvent.HakoSimAssetEvent_Stop:
                    StopCallback();
                    break;
                case HakoSimAssetEvent.HakoSimAssetEvent_Reset:
                    ResetCallback();
                    break;
                default:
                    break;
            }
        }
        private void StartCallback()
        {
            foreach (var obj in hakoObectList)
            {
                obj.EventStart();
            }
            // 動作版 _handle_start_event に合わせ、start_feedback の前に write_pdu_done を送る
            HakoCppWrapper.asset_notify_write_pdu_done(my_asset_name);
            HakoCppWrapper.asset_start_feedback(my_asset_name, true);
        }
        private void StopCallback()
        {
            foreach (var obj in hakoObectList)
            {
                obj.EventStop();
            }
            HakoCppWrapper.asset_stop_feedback(my_asset_name, true);
        }
        private void ResetCallback()
        {
            foreach (var obj in hakoObectList)
            {
                obj.EventReset();
            }
            HakoCppWrapper.asset_reset_feedback(my_asset_name, true);
        }
        public bool Execute()
        {
            //for heartbeat
            long w_time = HakoCppWrapper.get_wrold_time();
            HakoCppWrapper.asset_notify_simtime(my_asset_name, w_time);
            this.PollEvent();

            if (this.GetState() != SimulationState.Running)
            {
                return false;
            }

            /********************
             * Hakoniwa Time Sync
             ********************/
            if (HakoCppWrapper.asset_is_pdu_created() == false)
            {
                /* nothing to do */
                return false;
            }
            // [F1] 動作版(hakoniwa-armpi の hakoniwa_simulation_node.gd _tick_internal)に合わせ、
            //      per-asset の pdu_sync_mode を global の simulation_mode より「先」に判定する。
            //      従来は simulation_mode(常にTrue)を先に見たため sync 分岐に到達せず
            //      asset_notify_write_pdu_done() が一度も呼ばれず、conductor が PDU 同期バリアを
            //      解放できずに world 時刻が 0 から進まなかった（wtime=0 デッドロック）。
            //      sync 時は PDU 受信(EventTick)してから write_pdu_done を必ず送る。
            else if (HakoCppWrapper.asset_is_pdu_sync_mode(my_asset_name))
            {
                hako_pdu.EventTick();
                foreach (var obj in hakoObectList)
                {
                    obj.EventTick();
                }
                HakoCppWrapper.asset_notify_write_pdu_done(my_asset_name);
                return false;
            }
            else if (HakoCppWrapper.asset_is_simulation_mode())
            {
                long world_time = this.GetWorldTime();
                long next_asset_time_usec = this.asset_time_usec + this.delta_time_usec;
                if (next_asset_time_usec <= world_time)
                {
                    this.asset_time_usec = next_asset_time_usec;
                    HakoCppWrapper.asset_notify_simtime(my_asset_name, this.asset_time_usec);
                }
                else
                {
                    // can not do simulation because world time is slow...
                    //GD.Print($"Can not do simulation because world time is slow... asset_time={asset_time_usec} world_time={world_time}");
                    return false;
                }
                hako_pdu.EventTick();
                foreach (var obj in hakoObectList)
                {
                    obj.EventTick();
                }
                return true;
            }
            //GD.Print("Can not do simulation why??");
            return false;
        }

        public SimulationState GetState()
        {
            if (!isReady)
            {
                return SimulationState.Terminated;
            }
            HakoState state = HakoCppWrapper.simevent_get_state();
            switch (state)
            {
                case HakoState.Stopped:
                case HakoState.Resetting:
                    return SimulationState.Stopped;
                case HakoState.Runnable:
                    return SimulationState.Runnable;
                case HakoState.Running:
                    return SimulationState.Running;
                case HakoState.Stopping:
                    return SimulationState.Stopping;
                case HakoState.Error:
                case HakoState.Terminated:
                default:
                    return SimulationState.Terminated;
            }
        }


        public async Task<bool> RegisterOnHakoniwa()
        {
            if (isReady)
            {
                var ret = await pduManager.StartService();
                if (ret)
                {
                    isReady = HakoCppWrapper.asset_register_polling(my_asset_name);
//#if UNITY_EDITOR
                    GD.Print($"asset register result({isReady}): {my_asset_name}");
//#endif
                }
                else
                {
                    isReady = false;
//#if UNITY_EDITOR
                    GD.PrintErr($"pduManager StartService error: {my_asset_name}");
//#endif
                }
            }
            return isReady;
        }

        public Task<bool> UnRegisterOnHakoniwa()
        {
            if (isReady)
            {
                var ret = pduManager.StopService();
                if (ret == false)
                {
                    //TODO ERROR LOG
                }
                return Task.FromResult<bool>(HakoCppWrapper.asset_unregister(my_asset_name));
            }
            return Task.FromResult<bool>(false);
        }
        public long GetWorldTime()
        {
            if (isReady)
            {
                return HakoCppWrapper.get_wrold_time();
            }
            return 0;
        }

        public bool SimulationStart()
        {
            if (isReady)
            {
                return HakoCppWrapper.simevent_start();
            }
            return false;
        }

        public bool SimulationStop()
        {
            if (isReady)
            {
                return HakoCppWrapper.simevent_stop();
            }
            return false;
        }

        public bool SimulationReset()
        {
            if (isReady)
            {
                asset_time_usec = 0;
                return HakoCppWrapper.simevent_reset();
            }
            return false;
        }

        public IPduManager GetPduManager()
        {
            if (isReady)
            {
                return pduManager;
            }
            return null;
        }

        public IHakoCommunicationService GetHakoCommunicationService()
        {
            if (isReady)
            {
                return hako_pdu;
            }
            return null;
        }
    }
}
