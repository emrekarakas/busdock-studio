using System.Runtime.InteropServices;
using System.Text;
using BusDockStudio.Core.Services;

namespace BusDockStudio.Infrastructure;

public sealed class MacOSKeychainSecretStore : ISecretStore
{
    private const string ServiceName = "com.busdockstudio.azure-service-bus";
    private const string ItemLabel = "BusDock Studio Azure Service Bus";
    private const int Success = 0;
    private const int ItemNotFound = -25300;
    private const int DuplicateItem = -25299;

    public Task SetAsync(Guid profileId, string secret, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureMacOS();

        var query = CreateIdentityQuery(profileId);
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var secretData = Native.CFDataCreate(IntPtr.Zero, secretBytes, secretBytes.Length);
        if (secretData == IntPtr.Zero)
        {
            Array.Clear(secretBytes);
            query.Dispose();
            throw new InvalidOperationException("Could not allocate protected credential data.");
        }

        try
        {
            using var update = new NativeDictionary((Native.ValueData, secretData));
            var status = Native.SecItemUpdate(query.Handle, update.Handle);
            if (status == ItemNotFound)
            {
                using var service = NativeString.Create(ServiceName);
                using var account = NativeString.Create(AccountName(profileId));
                using var label = NativeString.Create(ItemLabel);
                using var add = new NativeDictionary(
                    (Native.SecClass, Native.ClassGenericPassword),
                    (Native.AttrService, service.Handle),
                    (Native.AttrAccount, account.Handle),
                    (Native.AttrLabel, label.Handle),
                    (Native.ValueData, secretData));
                status = Native.SecItemAdd(add.Handle, out var result);
                Native.ReleaseIfNeeded(result);
                if (status == DuplicateItem)
                {
                    status = Native.SecItemUpdate(query.Handle, update.Handle);
                }
            }

            ThrowIfFailed(status, "save");
        }
        finally
        {
            Native.CFRelease(secretData);
            Array.Clear(secretBytes);
            query.Dispose();
        }

        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(Guid profileId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureMacOS();

        using var service = NativeString.Create(ServiceName);
        using var account = NativeString.Create(AccountName(profileId));
        using var query = new NativeDictionary(
            (Native.SecClass, Native.ClassGenericPassword),
            (Native.AttrService, service.Handle),
            (Native.AttrAccount, account.Handle),
            (Native.ReturnData, Native.True),
            (Native.MatchLimit, Native.MatchLimitOne));

        var status = Native.SecItemCopyMatching(query.Handle, out var result);
        if (status == ItemNotFound)
        {
            return Task.FromResult<string?>(null);
        }

        ThrowIfFailed(status, "read");
        try
        {
            var length = checked((int)Native.CFDataGetLength(result));
            var bytes = new byte[length];
            if (length > 0)
            {
                Marshal.Copy(Native.CFDataGetBytePtr(result), bytes, 0, length);
            }

            try
            {
                return Task.FromResult<string?>(Encoding.UTF8.GetString(bytes));
            }
            finally
            {
                Array.Clear(bytes);
            }
        }
        finally
        {
            Native.ReleaseIfNeeded(result);
        }
    }

    public Task RemoveAsync(Guid profileId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureMacOS();

        using var query = CreateIdentityQuery(profileId);
        var status = Native.SecItemDelete(query.Handle);
        if (status != ItemNotFound)
        {
            ThrowIfFailed(status, "delete");
        }

        return Task.CompletedTask;
    }

    private static NativeDictionary CreateIdentityQuery(Guid profileId)
    {
        using var service = NativeString.Create(ServiceName);
        using var account = NativeString.Create(AccountName(profileId));
        return new NativeDictionary(
            (Native.SecClass, Native.ClassGenericPassword),
            (Native.AttrService, service.Handle),
            (Native.AttrAccount, account.Handle));
    }

    private static string AccountName(Guid profileId) => profileId.ToString("N");

    private static void EnsureMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("macOS Keychain is only available on macOS.");
        }
    }

    private static void ThrowIfFailed(int status, string operation)
    {
        if (status != Success)
        {
            throw new KeychainException($"Could not {operation} the credential in macOS Keychain (status {status}).");
        }
    }

    private sealed class NativeString : IDisposable
    {
        private NativeString(IntPtr handle) => Handle = handle;
        public IntPtr Handle { get; }

        public static NativeString Create(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var handle = Native.CFStringCreateWithBytes(
                IntPtr.Zero,
                bytes,
                bytes.Length,
                Native.Utf8Encoding,
                false);
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Could not allocate a native string.");
            }

            return new(handle);
        }

        public void Dispose() => Native.CFRelease(Handle);
    }

    private sealed class NativeDictionary : IDisposable
    {
        public NativeDictionary(params (IntPtr Key, IntPtr Value)[] pairs)
        {
            var keys = pairs.Select(pair => pair.Key).ToArray();
            var values = pairs.Select(pair => pair.Value).ToArray();
            Handle = Native.CFDictionaryCreate(
                IntPtr.Zero,
                keys,
                values,
                pairs.Length,
                Native.DictionaryKeyCallbacks,
                Native.DictionaryValueCallbacks);
            if (Handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Could not allocate a native dictionary.");
            }
        }

        public IntPtr Handle { get; }
        public void Dispose() => Native.CFRelease(Handle);
    }

    private static class Native
    {
        private const string SecurityFramework = "/System/Library/Frameworks/Security.framework/Security";
        private const string CoreFoundationFramework = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private static readonly IntPtr SecurityHandle = NativeLibrary.Load(SecurityFramework);
        private static readonly IntPtr CoreFoundationHandle = NativeLibrary.Load(CoreFoundationFramework);

        public const uint Utf8Encoding = 0x08000100;
        public static readonly IntPtr SecClass = GetObject(SecurityHandle, "kSecClass");
        public static readonly IntPtr ClassGenericPassword = GetObject(SecurityHandle, "kSecClassGenericPassword");
        public static readonly IntPtr AttrService = GetObject(SecurityHandle, "kSecAttrService");
        public static readonly IntPtr AttrAccount = GetObject(SecurityHandle, "kSecAttrAccount");
        public static readonly IntPtr AttrLabel = GetObject(SecurityHandle, "kSecAttrLabel");
        public static readonly IntPtr ValueData = GetObject(SecurityHandle, "kSecValueData");
        public static readonly IntPtr ReturnData = GetObject(SecurityHandle, "kSecReturnData");
        public static readonly IntPtr MatchLimit = GetObject(SecurityHandle, "kSecMatchLimit");
        public static readonly IntPtr MatchLimitOne = GetObject(SecurityHandle, "kSecMatchLimitOne");
        public static readonly IntPtr True = GetObject(CoreFoundationHandle, "kCFBooleanTrue");
        public static readonly IntPtr DictionaryKeyCallbacks = NativeLibrary.GetExport(CoreFoundationHandle, "kCFTypeDictionaryKeyCallBacks");
        public static readonly IntPtr DictionaryValueCallbacks = NativeLibrary.GetExport(CoreFoundationHandle, "kCFTypeDictionaryValueCallBacks");

        [DllImport(SecurityFramework)]
        public static extern int SecItemAdd(IntPtr attributes, out IntPtr result);

        [DllImport(SecurityFramework)]
        public static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);

        [DllImport(SecurityFramework)]
        public static extern int SecItemUpdate(IntPtr query, IntPtr attributesToUpdate);

        [DllImport(SecurityFramework)]
        public static extern int SecItemDelete(IntPtr query);

        [DllImport(CoreFoundationFramework)]
        public static extern IntPtr CFStringCreateWithBytes(
            IntPtr allocator,
            byte[] bytes,
            nint length,
            uint encoding,
            [MarshalAs(UnmanagedType.I1)] bool isExternalRepresentation);

        [DllImport(CoreFoundationFramework)]
        public static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);

        [DllImport(CoreFoundationFramework)]
        public static extern nint CFDataGetLength(IntPtr data);

        [DllImport(CoreFoundationFramework)]
        public static extern IntPtr CFDataGetBytePtr(IntPtr data);

        [DllImport(CoreFoundationFramework)]
        public static extern IntPtr CFDictionaryCreate(
            IntPtr allocator,
            IntPtr[] keys,
            IntPtr[] values,
            nint count,
            IntPtr keyCallbacks,
            IntPtr valueCallbacks);

        [DllImport(CoreFoundationFramework)]
        public static extern void CFRelease(IntPtr value);

        public static void ReleaseIfNeeded(IntPtr value)
        {
            if (value != IntPtr.Zero)
            {
                CFRelease(value);
            }
        }

        private static IntPtr GetObject(IntPtr library, string name) =>
            Marshal.ReadIntPtr(NativeLibrary.GetExport(library, name));
    }
}

public sealed class KeychainException(string message) : Exception(message);
