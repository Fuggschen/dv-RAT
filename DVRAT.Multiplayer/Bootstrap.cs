using System;
using System.Collections.Generic;
using DV.Customization.Gadgets;
using MPAPI;
using MPAPI.Interfaces;
using MPAPI.Types;

namespace DVRAT.Multiplayer
{
    /// <summary>Started through reflection by <see cref="MultiplayerSync"/> once the Multiplayer API is loaded.</summary>
    public static class Bootstrap
    {
        public static void Initialize()
        {
            if (!MultiplayerAPI.IsMultiplayerLoaded)
                throw new InvalidOperationException("the Multiplayer mod has not registered its API");
            MountSync.Hook();
            MultiplayerSync.Link = new Link();
        }

        private sealed class Link : IMultiplayerLink
        {
            public bool InGame => MountSync.InGame;
            public bool IsHost => MountSync.IsHost;
            public string LocalName => MountSync.LocalName;
            public void SendMount(GadgetBase gadget) => MountSync.SendMount(gadget);
            public void SendUnmount(string id) => MountSync.SendUnmount(id);
            public void SendSession(string id, string state) => MountSync.SendSession(id, state);
            public void SendSpeaker(string id, string state) => MountSync.SendSpeaker(id, state);
            public void SendMediaCommand(string id, string source, string command) => MountSync.SendMediaCommand(id, source, command);
            public IAudioEncoder CreateEncoder(string id) => MountSync.CreateEncoder(id);
            public IAudioDecoder CreateDecoder() => MountSync.CreateDecoder();
            public void Shutdown() => MountSync.Unhook();
        }
    }

    /// <summary>The host orders all changes: clients send to it, it applies, numbers and relays them.</summary>
    internal static class MountSync
    {
        private static IServer _server;
        private static IClient _client;
        private static uint _seq;
        // Newest applied Seq per gadget (client) or per player and gadget (host). See RatMountPacket.Seq.
        private static readonly Dictionary<string, uint> Applied = new Dictionary<string, uint>();
        // This player's name as the host knows it.
        private static string _name;

        public static string LocalName
        {
            get
            {
                if (_server != null && _client != null && _name == null)
                    _name = _server.GetPlayer(_client.PlayerId)?.Username;
                return _name;
            }
        }

        public static bool InGame
        {
            get => MultiplayerAPI.Instance != null && MultiplayerAPI.Instance.IsConnected;
        }

        public static bool IsHost
        {
            get => MultiplayerAPI.Instance != null && MultiplayerAPI.Instance.IsHost;
        }

        public static void Hook()
        {
            var api = MultiplayerAPI.Instance;
            // A tablet one player mounts appears on the others' trains, so everyone needs R.A.T.
            api.SetModCompatibility(RATMain.ModEntry.Info.Id, MultiplayerCompatibility.All);
            MultiplayerAPI.ServerStarted += OnServerStarted;
            MultiplayerAPI.ServerStopped += OnServerStopped;
            MultiplayerAPI.ClientStarted += OnClientStarted;
            MultiplayerAPI.ClientStopped += OnClientStopped;
            api.OnTick += OnTick;
            RATMain.Log($"Multiplayer {api.MultiplayerVersion} found, API {MultiplayerAPI.LoadedApiVersion} " +
                        $"(it supports {api.SupportedApiVersion})");
        }

        public static void Unhook()
        {
            MultiplayerAPI.ServerStarted -= OnServerStarted;
            MultiplayerAPI.ServerStopped -= OnServerStopped;
            MultiplayerAPI.ClientStarted -= OnClientStarted;
            MultiplayerAPI.ClientStopped -= OnClientStopped;
            if (MultiplayerAPI.Instance != null) MultiplayerAPI.Instance.OnTick -= OnTick;
        }

        private const float FeeCheckSeconds = 2f;
        private static uint _nextFeeCheck;
        private static string _sentFees;
        private static string _sentOdometers;

        private static void OnTick(uint tick)
        {
            if (_server == null || tick < _nextFeeCheck) return;
            _nextFeeCheck = tick + (uint)(FeeCheckSeconds * MultiplayerAPI.Instance.TICK_RATE);
            if (_server.PlayerCount <= 1) return;
            string fees = SharedFees.Describe();
            if (fees != _sentFees)
            {
                _sentFees = fees;
                _server.SendPacketToAll(new RatFeesPacket { Json = fees }, excludeSelf: true);
            }
            string odometers = Odometer.Describe();
            if (odometers != _sentOdometers)
            {
                _sentOdometers = odometers;
                _server.SendPacketToAll(new RatOdometerPacket { Json = odometers }, excludeSelf: true);
            }
        }

        private static void OnServerStarted(IServer server)
        {
            _server = server;
            _seq = 0;
            _sentFees = null;
            _sentOdometers = null;
            _nextFeeCheck = 0;
            Applied.Clear();
            server.RegisterPacket<RatMountPacket>(OnMountFromClient);
            server.RegisterPacket<RatUnmountPacket>(OnUnmountFromClient);
            server.RegisterPacket<RatSessionPacket>(OnSessionFromClient);
            server.RegisterPacket<RatSpeakerPacket>(OnSpeakerFromClient);
            server.RegisterPacket<RatMediaCommandPacket>((packet, sender) => RouteMediaCommand(packet));
            server.RegisterPacket<RatAudioPacket>(OnAudioFromClient);
            server.OnPlayerReady += SendAllMounts;
            server.OnPlayerDisconnected += ForgetPlayer;
            RATMain.Log("Multiplayer: hosting a game");
        }

        private static void OnServerStopped()
        {
            if (_server != null)
            {
                _server.OnPlayerReady -= SendAllMounts;
                _server.OnPlayerDisconnected -= ForgetPlayer;
            }
            _server = null;
            RATMain.Log("Multiplayer: hosting ended");
        }

        private static void OnClientStarted(IClient client)
        {
            _client = client;
            // The host's server already set these up.
            if (_server == null)
            {
                _seq = 0;
                Applied.Clear();
            }
            _name = null;
            client.RegisterPacket<RatMountPacket>(OnMountFromHost);
            client.RegisterPacket<RatUnmountPacket>(OnUnmountFromHost);
            client.RegisterPacket<RatNamePacket>(packet => _name = packet.Name);
            client.RegisterPacket<RatFeesPacket>(packet => SharedFees.Receive(packet.Json));
            client.RegisterPacket<RatOdometerPacket>(packet => Odometer.Receive(packet.Json));
            client.RegisterPacket<RatSessionPacket>(OnSessionFromHost);
            client.RegisterPacket<RatSpeakerPacket>(OnSpeakerFromHost);
            client.RegisterPacket<RatMediaCommandPacket>(packet => MultiplayerSync.RunMediaCommand(packet.Id, packet.Command));
            client.RegisterPacket<RatAudioPacket>(packet => MultiplayerSync.ReceiveAudio(packet.Id, packet.Epoch, packet.Seq, packet.Data));
            RATMain.Log($"Multiplayer: joined as player {client.PlayerId}");
        }

        private static void OnClientStopped()
        {
            _client = null;
            SharedFees.Clear();
            Odometer.Clear();
            TabletSync.ForgetPending();
            SpeakerSync.ForgetPending();
            RATMain.Log("Multiplayer: left the game");
        }

        public static void SendMount(GadgetBase gadget)
        {
            var packet = MountPacket(gadget);
            if (packet == null) return;
            packet.Seq = ++_seq;
            if (_server != null)
                _server.SendPacketToAll(packet, excludeSelf: true);
            else
                _client?.SendPacketToServer(packet);
            RATMain.Log($"Multiplayer: sent the mount of {packet.Prefab} {packet.Id}");
        }

        public static void SendUnmount(string id)
        {
            var packet = new RatUnmountPacket { Id = id, Seq = ++_seq };
            if (_server != null)
                _server.SendPacketToAll(packet, excludeSelf: true);
            else
                _client?.SendPacketToServer(packet);
            RATMain.Log($"Multiplayer: sent the removal of {id}");
        }

        public static void SendSession(string id, string state)
        {
            var packet = new RatSessionPacket { Id = id, State = state, Seq = ++_seq };
            if (_server != null)
                _server.SendPacketToAll(packet, excludeSelf: true);
            else
                _client?.SendPacketToServer(packet);
        }

        private static void OnSessionFromClient(RatSessionPacket packet, IPlayer sender)
        {
            if (!IsNewest(sender.PlayerId + ":s:" + packet.Id, packet.Seq)) return;
            TabletSync.Receive(packet.Id, packet.State);
            packet.Seq = ++_seq;
            _server.SendPacketToAll(packet, excludeSelf: true, excludePlayer: sender);
        }

        private static void OnSessionFromHost(RatSessionPacket packet)
        {
            if (!IsNewest("s:" + packet.Id, packet.Seq)) return;
            TabletSync.Receive(packet.Id, packet.State);
        }

        // The codec lives in its own DLL, loaded by reflection: a type built on a missing library breaks GetTypes over all loaded assemblies.
        private const string CodecFile = "DVRAT.Opus.dll";
        private const string ConcentusFile = "Concentus.dll";
        // Libraries Concentus needs that the game lacks. The mod folder is not searched by itself.
        private static readonly string[] CodecLibraries = { "Concentus", "System.Memory", "System.Buffers", "System.Numerics.Vectors" };
        // System.Memory wants 4.0.4.1, the game has 4.0.4.0, which works the same.
        private const string UnsafeLibrary = "System.Runtime.CompilerServices.Unsafe";
        private const string EncoderType = "DVRAT.Opus.OpusEncoderSink";
        private const string DecoderType = "DVRAT.Opus.OpusDecoderSource";
        private static System.Reflection.Assembly _codec;
        private static bool _codecFailed;

        public static IAudioEncoder CreateEncoder(string id)
        {
            // Packets are made on the reader thread, but the Multiplayer mod sends from the main thread only.
            Action<uint, uint, byte[]> send = (epoch, seq, data) =>
                UnityMainThreadDispatcher.Enqueue(() => SendAudio(new RatAudioPacket { Id = id, Epoch = epoch, Seq = seq, Data = data }));
            return CreateCodec<IAudioEncoder>(EncoderType, send);
        }

        public static IAudioDecoder CreateDecoder() => CreateCodec<IAudioDecoder>(DecoderType);

        private static System.Reflection.Assembly ResolveCodecLibrary(object sender, ResolveEventArgs args)
        {
            string name = new System.Reflection.AssemblyName(args.Name).Name;
            if (name == UnsafeLibrary)
            {
                foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
                    if (loaded.GetName().Name == name) return loaded;
                return null;
            }
            if (Array.IndexOf(CodecLibraries, name) < 0) return null;
            string path = System.IO.Path.Combine(RATMain.ModEntry.Path, name + ".dll");
            return System.IO.File.Exists(path) ? System.Reflection.Assembly.LoadFrom(path) : null;
        }

        private static T CreateCodec<T>(string type, params object[] args) where T : class
        {
            if (_codecFailed) return null;
            try
            {
                if (_codec == null)
                {
                    string folder = RATMain.ModEntry.Path;
                    string codec = System.IO.Path.Combine(folder, CodecFile);
                    string concentus = System.IO.Path.Combine(folder, ConcentusFile);
                    if (!System.IO.File.Exists(codec) || !System.IO.File.Exists(concentus))
                        throw new System.IO.FileNotFoundException($"{CodecFile} and {ConcentusFile} belong in the mod folder");
                    // Never removed: Concentus loads its libraries lazily.
                    AppDomain.CurrentDomain.AssemblyResolve += ResolveCodecLibrary;
                    _codec = System.Reflection.Assembly.LoadFrom(codec);
                }
                return (T)Activator.CreateInstance(_codec.GetType(type, true), args);
            }
            catch (Exception ex)
            {
                var cause = ex is System.Reflection.TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                _codecFailed = true;
                RATMain.LogWarning($"Multiplayer: the sound codec did not load ({cause.GetType().Name}: {cause.Message}); " +
                                   "tablets show what plays, but the other players do not hear it");
                return null;
            }
        }

        private static void SendAudio(RatAudioPacket packet)
        {
            if (_server != null)
                _server.SendPacketToAll(packet, reliable: false, excludeSelf: true);
            else
                _client?.SendPacketToServer(packet, reliable: false);
        }

        // The host plays it too and relays it.
        private static void OnAudioFromClient(RatAudioPacket packet, IPlayer sender)
        {
            MultiplayerSync.ReceiveAudio(packet.Id, packet.Epoch, packet.Seq, packet.Data);
            _server.SendPacketToAll(packet, reliable: false, excludeSelf: true, excludePlayer: sender);
        }

        public static void SendSpeaker(string id, string state)
        {
            var packet = new RatSpeakerPacket { Id = id, State = state, Seq = ++_seq };
            if (_server != null)
                _server.SendPacketToAll(packet, excludeSelf: true);
            else
                _client?.SendPacketToServer(packet);
        }

        private static void OnSpeakerFromClient(RatSpeakerPacket packet, IPlayer sender)
        {
            if (!IsNewest(sender.PlayerId + ":sp:" + packet.Id, packet.Seq)) return;
            SpeakerSync.Receive(packet.Id, packet.State);
            packet.Seq = ++_seq;
            _server.SendPacketToAll(packet, excludeSelf: true, excludePlayer: sender);
        }

        private static void OnSpeakerFromHost(RatSpeakerPacket packet)
        {
            if (!IsNewest("sp:" + packet.Id, packet.Seq)) return;
            SpeakerSync.Receive(packet.Id, packet.State);
        }

        public static void SendMediaCommand(string id, string source, string command)
        {
            var packet = new RatMediaCommandPacket { Id = id, Source = source, Command = command };
            if (_server != null)
                RouteMediaCommand(packet);
            else
                _client?.SendPacketToServer(packet);
        }

        // Relays the key to the player whose copy plays, or runs it when that is the host.
        private static void RouteMediaCommand(RatMediaCommandPacket packet)
        {
            if (packet.Source == LocalName)
            {
                MultiplayerSync.RunMediaCommand(packet.Id, packet.Command);
                return;
            }
            foreach (var player in _server.Players)
                if (player.Username == packet.Source)
                {
                    _server.SendPacketToPlayer(packet, player);
                    return;
                }
            RATMain.LogWarning($"Multiplayer: '{packet.Command}' for {packet.Source}, who is not in the game");
        }

        private static RatMountPacket MountPacket(GadgetBase gadget)
        {
            string id = GadgetMounts.IdOf(gadget);
            if (id == null || !GadgetMounts.TryDescribe(gadget, out string prefab, out string state, out var car)) return null;
            if (!MultiplayerAPI.Instance.TryGetNetId(car, out ushort carId))
            {
                RATMain.LogWarning($"Multiplayer: {car.ID} has no net ID, the others do not get {prefab} {id}");
                return null;
            }
            return new RatMountPacket { Id = id, Car = carId, Prefab = prefab, State = state, Session = TabletSync.StateOf(gadget) ?? "" };
        }

        private static void OnMountFromClient(RatMountPacket packet, IPlayer sender)
        {
            if (!IsNewest(sender.PlayerId + ":" + packet.Id, packet.Seq)) return;
            // The host's copy is the one saved with the game.
            ApplyMount(packet);
            packet.Seq = ++_seq;
            _server.SendPacketToAll(packet, excludeSelf: true, excludePlayer: sender);
        }

        private static void OnUnmountFromClient(RatUnmountPacket packet, IPlayer sender)
        {
            if (!IsNewest(sender.PlayerId + ":" + packet.Id, packet.Seq)) return;
            GadgetMounts.Unmount(packet.Id);
            packet.Seq = ++_seq;
            _server.SendPacketToAll(packet, excludeSelf: true, excludePlayer: sender);
        }

        // A rejoining player counts from 0, and the player number may go to someone else.
        private static void ForgetPlayer(IPlayer player)
        {
            MultiplayerSync.PlayerLeft(player.Username);
            string prefix = player.PlayerId + ":";
            foreach (string key in new List<string>(Applied.Keys))
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                    Applied.Remove(key);
        }

        private static void SendAllMounts(IPlayer player)
        {
            if (player.IsHost) return;
            _server.SendPacketToPlayer(new RatNamePacket { Name = player.Username }, player);
            _server.SendPacketToPlayer(new RatFeesPacket { Json = _sentFees ?? SharedFees.Describe() }, player);
            _server.SendPacketToPlayer(new RatOdometerPacket { Json = _sentOdometers ?? Odometer.Describe() }, player);
            int sent = 0;
            foreach (var gadget in GadgetMounts.AllMounted())
            {
                var packet = MountPacket(gadget);
                if (packet == null) continue;
                packet.Seq = ++_seq;
                _server.SendPacketToPlayer(packet, player);
                sent++;
            }
            if (sent > 0) RATMain.Log($"Multiplayer: sent {sent} mounted R.A.T. gadget(s) to {player.DisplayName}");
            // A state that arrives before its mount waits for the tablet.
            foreach (var state in TabletSync.AllStates())
                _server.SendPacketToPlayer(new RatSessionPacket { Id = state.Key, State = state.Value, Seq = ++_seq }, player);
            foreach (var state in SpeakerSync.AllStates())
                _server.SendPacketToPlayer(new RatSpeakerPacket { Id = state.Key, State = state.Value, Seq = ++_seq }, player);
        }

        private static void OnMountFromHost(RatMountPacket packet)
        {
            if (!IsNewest(packet.Id, packet.Seq)) return;
            ApplyMount(packet);
        }

        private static void OnUnmountFromHost(RatUnmountPacket packet)
        {
            if (!IsNewest(packet.Id, packet.Seq)) return;
            GadgetMounts.Unmount(packet.Id);
        }

        private static bool IsNewest(string key, uint seq)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (Applied.TryGetValue(key, out uint last) && last >= seq) return false;
            Applied[key] = seq;
            return true;
        }

        private static void ApplyMount(RatMountPacket packet)
        {
            if (!MultiplayerAPI.Instance.TryGetObjectFromNetId(packet.Car, out TrainCar car) || car == null)
            {
                RATMain.LogWarning($"Multiplayer: no car with net ID {packet.Car} for {packet.Prefab} {packet.Id}");
                return;
            }
            try
            {
                GadgetMounts.Mount(packet.Id, packet.Prefab, packet.State, car);
                TabletSync.Receive(packet.Id, packet.Session);
            }
            catch (Exception ex)
            {
                RATMain.LogError($"Multiplayer: mounting {packet.Prefab} {packet.Id} failed: {ex}");
            }
        }
    }
}
