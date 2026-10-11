using System.Runtime.InteropServices;

namespace Deckhand.Windows;

internal static class RecycleBin
{
    public static void Send(string path)
    {
        var root = Path.GetPathRoot(path)!;
        if (root.StartsWith("\\\\") || new DriveInfo(root).DriveType != DriveType.Fixed)
        {
            throw new NotSupportedException("Only local fixed-drive items can be recycled.");
        }

        var operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"),
                                                                                       true)!)!;
        IShellItem? item = null;
        try
        {
            var id = typeof(IShellItem).GUID;
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, nint.Zero, ref id, out item));
            // RECYCLEONDELETE, EARLYFAILURE, NOERRORUI, SILENT, NOCONFIRMATION.
            operation.SetOperationFlags(0x00080000 | 0x00100000 | 0x0400 | 0x0004 | 0x0010);
            operation.DeleteItem(item, nint.Zero);
            operation.PerformOperations();
            operation.GetAnyOperationsAborted(out var aborted);
            if (aborted || File.Exists(path) || Directory.Exists(path))
            {
                throw new IOException("The item was not recycled.");
            }
        }
        finally
        {
            if (item is not null)
            {
                Marshal.ReleaseComObject(item);
            }

            Marshal.ReleaseComObject(operation);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path,
                                                          nint bindContext,
                                                          ref Guid id,
                                                          out IShellItem item);

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
    }

    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(nint sink, out uint cookie);

        void Unadvise(uint cookie);

        void SetOperationFlags(uint flags);

        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);

        void SetProgressDialog(nint dialog);

        void SetProperties(nint properties);

        void SetOwnerWindow(nint window);

        void ApplyPropertiesToItem(IShellItem item);

        void ApplyPropertiesToItems(nint items);

        void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, nint sink);

        void RenameItems(nint items, [MarshalAs(UnmanagedType.LPWStr)] string name);

        void MoveItem(IShellItem item,
                      IShellItem destination,
                      [MarshalAs(UnmanagedType.LPWStr)] string? name,
                      nint sink);

        void MoveItems(nint items, IShellItem destination);

        void CopyItem(IShellItem item,
                      IShellItem destination,
                      [MarshalAs(UnmanagedType.LPWStr)] string? name,
                      nint sink);

        void CopyItems(nint items, IShellItem destination);

        void DeleteItem(IShellItem item, nint sink);

        void DeleteItems(nint items);

        void NewItem(IShellItem destination,
                     uint attributes,
                     [MarshalAs(UnmanagedType.LPWStr)] string name,
                     [MarshalAs(UnmanagedType.LPWStr)] string? template,
                     nint sink);

        void PerformOperations();

        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
}
