using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using NyssaAI.Results;

using NyssaAI.Configuration.Contracts;

namespace NyssaAI.Configuration.OsKeyring;

/// <summary>
/// KeyringMacOs reads and writes generic passwords in the macOS keychain through
/// <c>Security.framework</c>. A secret filed under service <c>S</c> and key <c>K</c> is the generic
/// password whose service attribute is <c>S</c> and whose account attribute is <c>K</c>, the same
/// addressing <c>security add-generic-password -s S -a K</c> uses, so an operator can place one
/// with the stock tool.
/// </summary>
/// <remarks>
/// <para>
/// The modern <c>SecItem</c> API is used rather than the simpler but long-deprecated
/// <c>SecKeychainAddGenericPassword</c> family, so queries are CoreFoundation dictionaries built
/// here by hand. Every CoreFoundation object created is released in the finally, including on the
/// failure paths, because <c>SecItemCopyMatching</c> hands back a +1-retained result the caller
/// owns.
/// </para>
/// <para>
/// The attribute keys are CFString globals exported by the framework, so each is read as a pointer
/// to a pointer rather than rebuilt locally.
/// </para>
/// <para>
/// <c>errSecItemNotFound</c> is the one status meaning "nothing filed here"; every other non-zero
/// status is reported as the store being unable to answer, so a misread status can never be
/// mistaken for an absent secret. That is the same distinction the Windows and Linux adapters draw,
/// and the bootstrap keyring source depends on it.
/// </para>
/// </remarks>
[SupportedOSPlatform("macos")]
internal sealed partial class KeyringMacOs : IKeyring
{
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";

    private const uint EncodingUtf8 = 0x08000100;
    private const int StatusSuccess = 0;
    private const int StatusItemNotFound = -25300;

    /// <inheritdoc/>
    public Result<string?> Get(string service, string key)
    {
        var target = KeyringAddress.Compose(service, key);
        if (target.IsFailure)
            return Result<string?>.Failure(target.Errors);

        // This is the adapter boundary: the loader throws when a framework or symbol is missing,
        // and every CoreFoundation object allocated here is released by the scope.
        var scope = new CoreFoundationScope();
        try
        {
            var query = scope.Dictionary(
                [
                    scope.Constant(Security, "kSecClass"),
                    scope.Constant(Security, "kSecAttrService"),
                    scope.Constant(Security, "kSecAttrAccount"),
                    scope.Constant(Security, "kSecReturnData"),
                    scope.Constant(Security, "kSecMatchLimit"),
                ],
                [
                    scope.Constant(Security, "kSecClassGenericPassword"),
                    scope.String(service),
                    scope.String(key),
                    scope.Constant(CoreFoundation, "kCFBooleanTrue"),
                    scope.Constant(Security, "kSecMatchLimitOne"),
                ]);

            var status = SecItemCopyMatching(query, out var found);
            if (status == StatusItemNotFound)
                return Result<string?>.Success(null);

            if (status != StatusSuccess)
                return Result<string?>.Failure(StoreUnavailable(status));

            scope.Own(found);
            return Result<string?>.Success(ReadData(found));
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return Result<string?>.Failure(FrameworkUnavailable(exception));
        }
        finally
        {
            scope.Dispose();
        }
    }

    /// <inheritdoc/>
    public Result Set(string service, string key, string secret)
    {
        var target = KeyringAddress.Compose(service, key);
        if (target.IsFailure)
            return Result.Failure(target.Errors);

        var scope = new CoreFoundationScope();
        var blob = Encoding.UTF8.GetBytes(secret);
        try
        {
            var value = scope.Data(blob);
            var address = scope.Dictionary(
                [
                    scope.Constant(Security, "kSecClass"),
                    scope.Constant(Security, "kSecAttrService"),
                    scope.Constant(Security, "kSecAttrAccount"),
                ],
                [
                    scope.Constant(Security, "kSecClassGenericPassword"),
                    scope.String(service),
                    scope.String(key),
                ]);

            // Update first: SecItemAdd against an address that already holds an item fails as a
            // duplicate rather than replacing it, and replacing is exactly what a re-key needs.
            var update = scope.Dictionary([scope.Constant(Security, "kSecValueData")], [value]);
            var status = SecItemUpdate(address, update);
            if (status == StatusItemNotFound)
            {
                var addition = scope.Dictionary(
                    [
                        scope.Constant(Security, "kSecClass"),
                        scope.Constant(Security, "kSecAttrService"),
                        scope.Constant(Security, "kSecAttrAccount"),
                        scope.Constant(Security, "kSecValueData"),
                    ],
                    [
                        scope.Constant(Security, "kSecClassGenericPassword"),
                        scope.String(service),
                        scope.String(key),
                        value,
                    ]);

                status = SecItemAdd(addition, out var added);
                scope.Own(added);
            }

            return status == StatusSuccess
                ? Result.Success()
                : Result.Failure(StoreNotWritable(status));
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return Result.Failure(FrameworkUnavailable(exception));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(blob);
            scope.Dispose();
        }
    }

    /// <inheritdoc/>
    public Result Delete(string service, string key)
    {
        var target = KeyringAddress.Compose(service, key);
        if (target.IsFailure)
            return Result.Failure(target.Errors);

        var scope = new CoreFoundationScope();
        try
        {
            var address = scope.Dictionary(
                [
                    scope.Constant(Security, "kSecClass"),
                    scope.Constant(Security, "kSecAttrService"),
                    scope.Constant(Security, "kSecAttrAccount"),
                ],
                [
                    scope.Constant(Security, "kSecClassGenericPassword"),
                    scope.String(service),
                    scope.String(key),
                ]);

            var status = SecItemDelete(address);
            return status is StatusSuccess or StatusItemNotFound
                ? Result.Success()
                : Result.Failure(StoreNotDeletable(status));
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return Result.Failure(FrameworkUnavailable(exception));
        }
        finally
        {
            scope.Dispose();
        }
    }

    /// <summary>The failure returned when the keychain refuses deletion.</summary>
    /// <param name="status">The OSStatus the framework returned.</param>
    public static Error StoreNotDeletable(int status) => Error.FromMessages(
        "The credential could not be deleted.",
        $"keyring: SecItemDelete failed with OSStatus {status}",
        ErrorCategory.Unavailable);

    /// <summary>The failure returned when the keychain reports anything but "not found" on a read.</summary>
    /// <param name="status">The <c>OSStatus</c> the framework returned.</param>
    public static Error StoreUnavailable(int status) => Error.FromMessages(
        "The credential store could not be read.",
        $"keyring: SecItemCopyMatching failed with OSStatus {status}",
        ErrorCategory.Unavailable);

    /// <summary>The failure returned when the keychain refuses a write.</summary>
    /// <param name="status">The <c>OSStatus</c> the framework returned.</param>
    public static Error StoreNotWritable(int status) => Error.FromMessages(
        "The credential store could not be written.",
        $"keyring: SecItemUpdate or SecItemAdd failed with OSStatus {status}",
        ErrorCategory.Unavailable);

    /// <summary>The failure returned when the system frameworks could not be loaded.</summary>
    /// <param name="exception">The loader failure.</param>
    public static Error FrameworkUnavailable(Exception exception) => Error.FromMessages(
        "The credential store is not available on this machine.",
        $"keyring: {exception.GetType().Name}: {exception.Message}",
        ErrorCategory.Unavailable);

    private static unsafe string? ReadData(nint data)
    {
        if (data == 0)
            return null;

        var bytes = CFDataGetBytePtr(data);
        var length = CFDataGetLength(data);
        return bytes == 0 || length <= 0
            ? string.Empty
            : Encoding.UTF8.GetString(new ReadOnlySpan<byte>((void*)bytes, (int)length));
    }

    /// <summary>
    /// Tracks every CoreFoundation object and framework handle created for one call, so the
    /// finally releases them in one place and no failure path leaks.
    /// </summary>
    private sealed class CoreFoundationScope : IDisposable
    {
        private readonly List<nint> _owned = [];
        private readonly Dictionary<string, nint> _libraries = new(StringComparer.Ordinal);

        public void Own(nint handle)
        {
            if (handle != 0)
                _owned.Add(handle);
        }

        public nint Constant(string library, string symbol) =>
            // The exported symbol is the address of a CFStringRef, not the CFStringRef itself.
            Marshal.ReadIntPtr(NativeLibrary.GetExport(Library(library), symbol));

        public nint String(string value)
        {
            var utf8 = Marshal.StringToCoTaskMemUTF8(value);
            try
            {
                var handle = CFStringCreateWithCString(0, utf8, EncodingUtf8);
                Own(handle);
                return handle;
            }
            finally
            {
                Marshal.FreeCoTaskMem(utf8);
            }
        }

        public nint Data(byte[] bytes)
        {
            var handle = CFDataCreate(0, bytes, bytes.Length);
            Own(handle);
            return handle;
        }

        public nint Dictionary(nint[] keys, nint[] values)
        {
            var coreFoundation = Library(CoreFoundation);
            var handle = CFDictionaryCreate(
                0,
                keys,
                values,
                keys.Length,
                NativeLibrary.GetExport(coreFoundation, "kCFTypeDictionaryKeyCallBacks"),
                NativeLibrary.GetExport(coreFoundation, "kCFTypeDictionaryValueCallBacks"));
            Own(handle);
            return handle;
        }

        public void Dispose()
        {
            // Released newest first, so a dictionary goes before the strings it retains.
            for (var index = _owned.Count - 1; index >= 0; index--)
                CFRelease(_owned[index]);

            _owned.Clear();
            foreach (var library in _libraries.Values)
                NativeLibrary.Free(library);

            _libraries.Clear();
        }

        private nint Library(string path)
        {
            if (_libraries.TryGetValue(path, out var handle))
                return handle;

            handle = NativeLibrary.Load(path);
            _libraries[path] = handle;
            return handle;
        }
    }

    [LibraryImport(Security, EntryPoint = "SecItemCopyMatching")]
    private static partial int SecItemCopyMatching(nint query, out nint result);

    [LibraryImport(Security, EntryPoint = "SecItemAdd")]
    private static partial int SecItemAdd(nint attributes, out nint result);

    [LibraryImport(Security, EntryPoint = "SecItemDelete")]
    private static partial int SecItemDelete(nint query);

    [LibraryImport(Security, EntryPoint = "SecItemUpdate")]
    private static partial int SecItemUpdate(nint query, nint attributesToUpdate);

    [LibraryImport(CoreFoundation, EntryPoint = "CFStringCreateWithCString")]
    private static partial nint CFStringCreateWithCString(nint allocator, nint cString, uint encoding);

    [LibraryImport(CoreFoundation, EntryPoint = "CFDataCreate")]
    private static partial nint CFDataCreate(nint allocator, [In] byte[] bytes, nint length);

    [LibraryImport(CoreFoundation, EntryPoint = "CFDictionaryCreate")]
    private static partial nint CFDictionaryCreate(
        nint allocator, [In] nint[] keys, [In] nint[] values, nint count, nint keyCallBacks, nint valueCallBacks);

    [LibraryImport(CoreFoundation, EntryPoint = "CFDataGetBytePtr")]
    private static partial nint CFDataGetBytePtr(nint data);

    [LibraryImport(CoreFoundation, EntryPoint = "CFDataGetLength")]
    private static partial nint CFDataGetLength(nint data);

    [LibraryImport(CoreFoundation, EntryPoint = "CFRelease")]
    private static partial void CFRelease(nint reference);
}
