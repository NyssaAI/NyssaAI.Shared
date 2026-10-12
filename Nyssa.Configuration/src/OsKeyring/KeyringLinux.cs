using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NyssaAI.Results;
using NyssaAI.Configuration.Contracts;


namespace NyssaAI.Configuration.OsKeyring;

/// <summary>
/// KeyringLinux reads secrets from the Secret Service through <c>libsecret</c>. A secret filed
/// under service <c>S</c> and key <c>K</c> is the item whose attributes are
/// <c>service=S</c> and <c>username=K</c> under the generic schema, the same addressing
/// <c>secret-tool</c> and the Python <c>keyring</c> package use, so an operator can place a
/// secret with <c>secret-tool store --label=... service S username K</c>.
/// </summary>
/// <remarks>
/// <para>
/// The lookup returns a pointer that libsecret allocated and that only libsecret's own free may
/// release; it is decoded and freed here, never marshalled as a managed string, which would hand
/// a glib allocation to the CoTaskMem allocator. A populated <c>GError</c> is the store being
/// unreachable -- no session bus, no daemon, a locked collection -- and is a failure, not a miss.
/// A missing <c>libsecret-1.so.0</c> or <c>libglib-2.0.so.0</c> is the same failure.
/// </para>
/// <para>
/// The schema is built in unmanaged memory rather than through <c>secret_schema_new</c>, which
/// is variadic and cannot be called through <c>LibraryImport</c> portably.
/// </para>
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed partial class KeyringLinux : IKeyring
{
    private const string LibSecret = "libsecret-1.so.0";
    private const string LibGlib = "libglib-2.0.so.0";
    private const int SecretSearchAll = 1;
    private const string SchemaName = "org.freedesktop.Secret.Generic";
    private const string ServiceAttribute = "service";
    private const string KeyAttribute = "username";

    // SecretSchema: name pointer, flags, 32 attribute slots of (name pointer, type), then reserved
    // fields; every slot is pointer-aligned, so the size is 74 pointers on any width.
    private const int SchemaAttributeSlots = 32;
    private static readonly nuint SchemaSize = (nuint)(74 * nint.Size);

    /// <inheritdoc/>
    public Result<string?> Get(string service, string key)
    {
        var target = KeyringAddress.Compose(service, key);
        if (target.IsFailure)
            return Result<string?>.Failure(target.Errors);

        // This is the adapter boundary: the loader throws when a library or symbol is missing, and
        // every native allocation made here is released in the finally regardless of outcome.
        nint glib = 0, schema = 0, schemaName = 0, serviceName = 0, keyName = 0, serviceValue = 0, keyValue = 0, table = 0;
        try
        {
            glib = NativeLibrary.Load(LibGlib);
            var hash = NativeLibrary.GetExport(glib, "g_str_hash");
            var equal = NativeLibrary.GetExport(glib, "g_str_equal");

            schemaName = Marshal.StringToCoTaskMemUTF8(SchemaName);
            serviceName = Marshal.StringToCoTaskMemUTF8(ServiceAttribute);
            keyName = Marshal.StringToCoTaskMemUTF8(KeyAttribute);
            serviceValue = Marshal.StringToCoTaskMemUTF8(service);
            keyValue = Marshal.StringToCoTaskMemUTF8(key);

            schema = BuildSchema(schemaName, serviceName, keyName);

            table = HashTableNew(hash, equal);
            HashTableInsert(table, serviceName, serviceValue);
            HashTableInsert(table, keyName, keyValue);

            var password = SecretPasswordLookup(schema, table, 0, out var error);
            if (error != 0)
            {
                var failure = StoreUnavailable(ReadErrorCode(error), ReadErrorMessage(error));
                ErrorFree(error);
                if (password != 0)
                    SecretPasswordFree(password);
                return Result<string?>.Failure(failure);
            }

            if (password == 0)
                return Result<string?>.Success(null);

            var secret = Marshal.PtrToStringUTF8(password);
            SecretPasswordFree(password);
            return Result<string?>.Success(secret);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return Result<string?>.Failure(LibraryUnavailable(exception));
        }
        finally
        {
            if (table != 0)
                HashTableUnref(table);
            if (schema != 0)
                FreeSchema(schema);
            Marshal.FreeCoTaskMem(schemaName);
            Marshal.FreeCoTaskMem(serviceName);
            Marshal.FreeCoTaskMem(keyName);
            Marshal.FreeCoTaskMem(serviceValue);
            Marshal.FreeCoTaskMem(keyValue);
            if (glib != 0)
            {
                NativeLibrary.Free(glib);
            }
        }
    }

    /// <inheritdoc/>
    public Result Set(string service, string key, string secret)
    {
        var target = KeyringAddress.Compose(service, key);
        if (target.IsFailure)
            return Result.Failure(target.Errors);

        // Mirrors Get: the same schema and attribute table address the item, so a value stored
        // here is found by the same lookup and by secret-tool with the same arguments.
        nint glib = 0, schema = 0, schemaName = 0, serviceName = 0, keyName = 0, serviceValue = 0, keyValue = 0;
        nint table = 0, label = 0, password = 0;
        try
        {
            glib = NativeLibrary.Load(LibGlib);
            var hash = NativeLibrary.GetExport(glib, "g_str_hash");
            var equal = NativeLibrary.GetExport(glib, "g_str_equal");

            schemaName = Marshal.StringToCoTaskMemUTF8(SchemaName);
            serviceName = Marshal.StringToCoTaskMemUTF8(ServiceAttribute);
            keyName = Marshal.StringToCoTaskMemUTF8(KeyAttribute);
            serviceValue = Marshal.StringToCoTaskMemUTF8(service);
            keyValue = Marshal.StringToCoTaskMemUTF8(key);
            label = Marshal.StringToCoTaskMemUTF8(target.Value);
            password = Marshal.StringToCoTaskMemUTF8(secret);

            schema = BuildSchema(schemaName, serviceName, keyName);

            table = HashTableNew(hash, equal);
            HashTableInsert(table, serviceName, serviceValue);
            HashTableInsert(table, keyName, keyValue);

            // The default collection is chosen by passing no collection at all.
            var stored = SecretPasswordStore(schema, table, 0, label, password, 0, out var error);
            if (error != 0)
            {
                var failure = StoreNotWritable(ReadErrorCode(error), ReadErrorMessage(error));
                ErrorFree(error);
                return Result.Failure(failure);
            }

            return stored != 0
                ? Result.Success()
                : Result.Failure(StoreNotWritable(0, "the Secret Service refused the item without reporting an error"));
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return Result.Failure(LibraryUnavailable(exception));
        }
        finally
        {
            if (table != 0)
                HashTableUnref(table);
            if (schema != 0)
                FreeSchema(schema);
            Marshal.FreeCoTaskMem(schemaName);
            Marshal.FreeCoTaskMem(serviceName);
            Marshal.FreeCoTaskMem(keyName);
            Marshal.FreeCoTaskMem(serviceValue);
            Marshal.FreeCoTaskMem(keyValue);
            Marshal.FreeCoTaskMem(label);
            if (password != 0)
            {
                // The password buffer holds the secret; clear it before it returns to the allocator.
                ZeroUtf8(password, secret.Length);
                Marshal.FreeCoTaskMem(password);
            }

            if (glib != 0)
            {
                NativeLibrary.Free(glib);
            }
        }
    }

    /// <inheritdoc/>
    public Result Delete(string service, string key)
    {
        var target = KeyringAddress.Compose(service, key);
        if (target.IsFailure)
            return Result.Failure(target.Errors);

        // This is the adapter boundary: the loader throws when a library or symbol is missing, and
        // every native allocation made here is released in the finally regardless of outcome.
        nint glib = 0, schema = 0, schemaName = 0, serviceName = 0, keyName = 0, serviceValue = 0, keyValue = 0, table = 0;
        nint items = 0;
        try
        {
            glib = NativeLibrary.Load(LibGlib);
            var hash = NativeLibrary.GetExport(glib, "g_str_hash");
            var equal = NativeLibrary.GetExport(glib, "g_str_equal");

            schemaName = Marshal.StringToCoTaskMemUTF8(SchemaName);
            serviceName = Marshal.StringToCoTaskMemUTF8(ServiceAttribute);
            keyName = Marshal.StringToCoTaskMemUTF8(KeyAttribute);
            serviceValue = Marshal.StringToCoTaskMemUTF8(service);
            keyValue = Marshal.StringToCoTaskMemUTF8(key);

            schema = BuildSchema(schemaName, serviceName, keyName);

            table = HashTableNew(hash, equal);
            HashTableInsert(table, serviceName, serviceValue);
            HashTableInsert(table, keyName, keyValue);

            // Search all matching items, including locked ones. password_clearv_sync only removes
            // unlocked items and can silently leave a locked credential behind.
            items = SecretServiceSearch(0, schema, table, SecretSearchAll, 0, out var error);
            if (error != 0)
            {
                var failure = StoreNotDeletable(ReadErrorCode(error), ReadErrorMessage(error));
                ErrorFree(error);
                return Result.Failure(failure);
            }

            // GList contains data, next and previous pointers. Searching does not load secret values.
            for (var node = items; node != 0; node = Marshal.ReadIntPtr(node, nint.Size))
            {
                var deleted = SecretItemDelete(Marshal.ReadIntPtr(node), 0, out error);
                if (error != 0)
                {
                    var failure = StoreNotDeletable(ReadErrorCode(error), ReadErrorMessage(error));
                    ErrorFree(error);
                    return Result.Failure(failure);
                }

                if (deleted == 0)
                    return Result.Failure(StoreNotDeletable(0, "the Secret Service refused deletion without reporting an error"));
            }

            return Result.Success();
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return Result.Failure(LibraryUnavailable(exception));
        }
        finally
        {
            for (var node = items; node != 0; node = Marshal.ReadIntPtr(node, nint.Size))
                ObjectUnref(Marshal.ReadIntPtr(node));
            if (items != 0)
                ListFree(items);
            if (table != 0)
                HashTableUnref(table);
            if (schema != 0)
                FreeSchema(schema);
            Marshal.FreeCoTaskMem(schemaName);
            Marshal.FreeCoTaskMem(serviceName);
            Marshal.FreeCoTaskMem(keyName);
            Marshal.FreeCoTaskMem(serviceValue);
            Marshal.FreeCoTaskMem(keyValue);
            if (glib != 0)
            {
                NativeLibrary.Free(glib);
            }
        }
    }

    /// <summary>The failure returned when the Secret Service refuses deletion or cannot search for the item.</summary>
    /// <param name="code">The GError code, or zero when deletion was refused without an error.</param>
    /// <param name="message">The store's diagnostic message.</param>
    public static Error StoreNotDeletable(int code, string? message) => Error.FromMessages(
        "The credential could not be deleted.",
        $"keyring: the Secret Service reported deletion error {code}: {message}",
        ErrorCategory.Unavailable);

    /// <summary>The failure returned when the Secret Service could not answer.</summary>
    /// <param name="code">The <c>GError</c> code.</param>
    /// <param name="message">The <c>GError</c> message, which describes the connection, never a secret.</param>
    public static Error StoreUnavailable(int code, string? message) => Error.FromMessages(
        "The credential store could not be read.",
        $"keyring: the Secret Service reported error {code}: {message}",
        ErrorCategory.Unavailable);

    /// <summary>The failure returned when libsecret or glib is not installed.</summary>
    /// <param name="exception">The loader failure.</param>
    public static Error LibraryUnavailable(Exception exception) => Error.FromMessages(
        "The credential store is not available on this machine.",
        $"keyring: {exception.GetType().Name}: {exception.Message}",
        ErrorCategory.Unavailable);

    /// <summary>The failure returned when the Secret Service refused to store an item.</summary>
    /// <param name="code">The <c>GError</c> code, or zero when the call failed without one.</param>
    /// <param name="message">The <c>GError</c> message, which describes the connection, never a secret.</param>
    public static Error StoreNotWritable(int code, string? message) => Error.FromMessages(
        "The credential store could not be written.",
        $"keyring: the Secret Service reported error {code}: {message}",
        ErrorCategory.Unavailable);

    private static unsafe void ZeroUtf8(nint buffer, int characters) =>
        new Span<byte>((void*)buffer, characters).Clear();

    private static unsafe nint BuildSchema(nint schemaName, nint serviceName, nint keyName)
    {
        var schema = (byte*)NativeMemory.AllocZeroed(SchemaSize);
        var slots = (nint*)schema;
        slots[0] = schemaName;
        // slots[1] holds the flags (SECRET_SCHEMA_NONE = 0) and stays zero.
        // Each attribute is two pointer-sized slots: the name and the type, STRING being 0.
        slots[2] = serviceName;
        slots[4] = keyName;
        // The third attribute's null name terminates the list; the memory is already zero.
        _ = SchemaAttributeSlots;
        return (nint)schema;
    }

    private static unsafe void FreeSchema(nint schema) => NativeMemory.Free((void*)schema);

    private static unsafe int ReadErrorCode(nint error) => *(int*)((byte*)error + sizeof(uint));

    private static unsafe string? ReadErrorMessage(nint error)
    {
        // GError: guint32 domain, gint code, gchar* message; the pointer sits at the next
        // pointer-aligned offset after the two 32-bit fields.
        var messageOffset = Math.Max(2 * sizeof(uint), nint.Size);
        var message = *(nint*)((byte*)error + messageOffset);
        return message == 0 ? null : Marshal.PtrToStringUTF8(message);
    }

    [LibraryImport(LibSecret, EntryPoint = "secret_password_lookupv_sync")]
    private static partial nint SecretPasswordLookup(nint schema, nint attributes, nint cancellable, out nint error);

    [LibraryImport(LibSecret, EntryPoint = "secret_password_storev_sync")]
    private static partial int SecretPasswordStore(
        nint schema, nint attributes, nint collection, nint label, nint password, nint cancellable, out nint error);

    [LibraryImport(LibSecret, EntryPoint = "secret_service_search_sync")]
    private static partial nint SecretServiceSearch(
        nint service, nint schema, nint attributes, int flags, nint cancellable, out nint error);

    [LibraryImport(LibSecret, EntryPoint = "secret_item_delete_sync")]
    private static partial int SecretItemDelete(nint item, nint cancellable, out nint error);

    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_object_unref")]
    private static partial void ObjectUnref(nint instance);

    [LibraryImport(LibGlib, EntryPoint = "g_list_free")]
    private static partial void ListFree(nint list);

    [LibraryImport(LibSecret, EntryPoint = "secret_password_free")]
    private static partial void SecretPasswordFree(nint password);

    [LibraryImport(LibGlib, EntryPoint = "g_hash_table_new")]
    private static partial nint HashTableNew(nint hashFunction, nint equalFunction);

    [LibraryImport(LibGlib, EntryPoint = "g_hash_table_insert")]
    private static partial int HashTableInsert(nint table, nint key, nint value);

    [LibraryImport(LibGlib, EntryPoint = "g_hash_table_unref")]
    private static partial void HashTableUnref(nint table);

    [LibraryImport(LibGlib, EntryPoint = "g_error_free")]
    private static partial void ErrorFree(nint error);
}
