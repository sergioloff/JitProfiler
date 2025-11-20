namespace TestApplication
{
    using System;
    using System.IO.MemoryMappedFiles;
    using System.IO;

    public class IpcFlagMap : IDisposable
    {
        public const string DefaultJitProfilerId = "SIG_JITPROFILER";
        private readonly string mapName;
        private MemoryMappedFile mmf;
        private MemoryMappedViewAccessor accessor;

        // Length for 1 int32 = 4 bytes
        private const long FLAG_LENGTH = 4;

        public IpcFlagMap(string mapName = DefaultJitProfilerId)
        {
            this.mapName = mapName;
            Initialize();
        }

        // Create and initialize the memory-mapped file
        private void Initialize()
        {
            // Try to open if already exists, else create
            mmf = MemoryMappedFile.CreateOrOpen(mapName, FLAG_LENGTH);
            accessor = mmf.CreateViewAccessor(0, FLAG_LENGTH);
            SetFlag(0);
        }

        // Set the int32 flag at offset 0
        public void SetFlag(int value)
        {
            if (accessor == null) throw new InvalidOperationException("Not initialized");
            accessor.Write(0, value);
            accessor.Flush();
        }

        // Get the int32 flag at offset 0 (optional)
        public int GetFlag()
        {
            if (accessor == null) throw new InvalidOperationException("Not initialized");
            return accessor.ReadInt32(0);
        }

        // Clean up resources
        public void Dispose()
        {
            accessor?.Dispose();
            mmf?.Dispose();
        }
    }
}
