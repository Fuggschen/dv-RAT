using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DV.Customization.Gadgets;
using UnityModManagerNet;

namespace DVRAT
{
    /// <summary>Implemented by DVRAT.Multiplayer.dll with the Multiplayer mod's API.</summary>
    public interface IMultiplayerLink
    {
        bool InGame { get; }

        bool IsHost { get; }

        /// <summary>Null until the host told it.</summary>
        string LocalName { get; }

        void SendMount(GadgetBase gadget);

        void SendUnmount(string id);

        /// <summary>See <see cref="TabletSync"/>.</summary>
        void SendSession(string id, string state);

        /// <summary>Goes to the copy of <paramref name="source"/>, which plays.</summary>
        void SendMediaCommand(string id, string source, string command);

        /// <summary>See <see cref="SpeakerSync"/>.</summary>
        void SendSpeaker(string id, string state);

        /// <summary>Null when there is no codec.</summary>
        IAudioEncoder CreateEncoder(string id);

        /// <summary>Null when there is no codec.</summary>
        IAudioDecoder CreateDecoder();

        void Shutdown();
    }

    /// <summary>Takes interleaved stereo samples at this PC's rate on the track's reader thread. A new <c>epoch</c> is the next song or station.</summary>
    public interface IAudioEncoder
    {
        void Write(uint epoch, float[] samples, int count);
    }

    /// <summary>Returns samples at this PC's rate. A null packet is a lost one and is filled in.</summary>
    public interface IAudioDecoder
    {
        float[] Decode(byte[] packet);
    }

    /// <summary>See <see cref="MediaPlayer.RunCommand"/>.</summary>
    public static class MediaCommands
    {
        public const string Play = "play", Pause = "pause", Next = "next", Previous = "previous", Stop = "stop",
            ShuffleOn = "shuffle on", ShuffleOff = "shuffle off";
    }

    /// <summary>
    /// DVRAT.dll must not reference the Multiplayer API: UnityModManager's GetTypes fails on such types without the mod.
    /// The API code lives in DVRAT.Multiplayer.dll, loaded only when the mod and its API are present
    /// (the Multiplayer wiki's "Shim With Integration"). Its Bootstrap sets <see cref="Link"/>.
    /// </summary>
    public static class MultiplayerSync
    {
        private const string ModId = "Multiplayer";
        private const string ApiAssembly = "MultiplayerAPI";
        private const string IntegrationFile = "DVRAT.Multiplayer.dll";
        private const string BootstrapType = "DVRAT.Multiplayer.Bootstrap";
        private const string InitializeMethod = "Initialize";

        /// <summary>Null without Multiplayer.</summary>
        public static IMultiplayerLink Link { get; set; }

        public static bool Available => Link != null;

        public static bool InGame => Link != null && Link.InGame;

        /// <summary>False in single player.</summary>
        public static bool IsHost => Link != null && Link.IsHost;

        public static void Initialize()
        {
            if (Link != null) return;
            var mod = UnityModManager.FindMod(ModId);
            if (mod == null || !mod.Enabled) return;
            if (!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == ApiAssembly))
            {
                RATMain.LogWarning("Multiplayer is enabled, but its API is not loaded; tablets stay unsynced");
                return;
            }
            string path = Path.Combine(RATMain.ModEntry.Path, IntegrationFile);
            if (!File.Exists(path))
            {
                RATMain.LogWarning($"Multiplayer found, but {IntegrationFile} is missing from the mod folder; tablets stay unsynced");
                return;
            }
            // The integration references DVRAT, so give it the loaded copy instead of a second one.
            AppDomain.CurrentDomain.AssemblyResolve += ResolveSelf;
            try
            {
                var initialize = Assembly.LoadFile(path).GetType(BootstrapType)
                    ?.GetMethod(InitializeMethod, BindingFlags.Public | BindingFlags.Static);
                if (initialize == null)
                {
                    RATMain.LogWarning($"{IntegrationFile} has no {BootstrapType}.{InitializeMethod}; tablets stay unsynced");
                    return;
                }
                initialize.Invoke(null, null);
            }
            catch (Exception ex)
            {
                var cause = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                RATMain.LogWarning($"Multiplayer {mod.Info.Version} found, but R.A.T.'s part for it did not start: " +
                                   $"{cause.Message}. Tablets stay unsynced.");
                Link = null;
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= ResolveSelf;
            }
        }

        private static Assembly ResolveSelf(object sender, ResolveEventArgs args)
        {
            var self = typeof(MultiplayerSync).Assembly;
            return new AssemblyName(args.Name).Name == self.GetName().Name ? self : null;
        }

        /// <summary>The others get a copy if it is R.A.T.'s.</summary>
        public static void OnLocalMount(GadgetBase gadget)
        {
            if (!InGame || GadgetMounts.IdOf(gadget) == null) return;
            Link.SendMount(gadget);
        }

        public static void OnLocalUnmount(GadgetBase gadget)
        {
            if (!InGame) return;
            string id = GadgetMounts.IdOf(gadget);
            if (id != null) Link.SendUnmount(id);
        }

        /// <summary>Null outside a game or until the host told it.</summary>
        public static string LocalName => InGame ? Link.LocalName : null;

        /// <summary>Goes to the copy that plays, see <see cref="MediaPlayer"/>.</summary>
        public static void SendMediaCommand(string id, string source, string command)
        {
            if (InGame) Link.SendMediaCommand(id, source, command);
        }

        /// <summary>Null outside a game or without a codec.</summary>
        public static IAudioEncoder CreateEncoder(string id) => InGame ? Link.CreateEncoder(id) : null;

        /// <summary>Null outside a game or without a codec.</summary>
        public static IAudioDecoder CreateDecoder() => InGame ? Link.CreateDecoder() : null;

        public static void ReceiveAudio(string id, uint epoch, uint seq, byte[] packet) =>
            TabletSync.Find(id)?.Media?.ReceiveAudio(epoch, seq, packet);

        public static void RunMediaCommand(string id, string command) => TabletSync.Find(id)?.Media?.RunCommand(command);

        /// <summary>Host only. Stops what played from the player's copies.</summary>
        public static void PlayerLeft(string player) => TabletSync.ForEachTablet(tablet => tablet.Media?.SourceLeft(player));

        public static void SendSpeaker(string id, string state)
        {
            if (InGame) Link.SendSpeaker(id, state);
        }

        public static void SendSession(string id, string state)
        {
            if (InGame) Link.SendSession(id, state);
        }

        public static void Shutdown()
        {
            Link?.Shutdown();
            Link = null;
        }
    }
}
