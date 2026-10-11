using System.Runtime.InteropServices;
using Deckhand.Agent.Settings;

namespace Deckhand.App;

/// <summary>Stores generic credentials in Windows Credential Manager.</summary>
internal sealed class WindowsSecretStore : ISecretStore
{
    private const uint GenericCredential = 1;

    public string? Read(string name)
    {
        if (!CredRead(name, GenericCredential, 0, out var pointer))
        {
            throw new InvalidOperationException("The API key was not found in Windows Credential Manager.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            return Marshal.PtrToStringUni(credential.Blob, checked((int)credential.BlobSize / 2));
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void Write(string name, string secret)
    {
        if (secret.Length > 1280)
        {
            throw new ArgumentException("The API key is too long for Windows Credential Manager (maximum 1,280 characters).");
        }

        var blob = Marshal.StringToCoTaskMemUni(secret);
        try
        {
            var credential = new Credential
            {
                Type = GenericCredential,
                TargetName = name,
                BlobSize = checked((uint)secret.Length * 2),
                Blob = blob,
                Persist = 2, // CRED_PERSIST_LOCAL_MACHINE: current user, across logon sessions.
                UserName = "Deckhand",
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new IOException("Could not save the API key in Windows Credential Manager.");
            }
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUnicode(blob);
        }
    }

    public void Delete(string name)
    {
        if (!CredDelete(name, GenericCredential, 0) && Marshal.GetLastWin32Error() != 1168) // ERROR_NOT_FOUND
        {
            throw new IOException("Could not remove the API key from Windows Credential Manager.");
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint BlobSize;
        public IntPtr Blob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);
}
