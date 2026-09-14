namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System.Collections.Generic;
    using System.Reflection;

    /// <summary>
    ///     An ordered command-discovery source consulted while a shell applies
    ///     its auto-command registration. Providers sit between the generated
    ///     catalog probe and the reflection compatibility walk: an assembly
    ///     that produced a non-empty catalog never reaches a provider, and a
    ///     provider that claims an assembly replaces the reflection walk for
    ///     it. The editor-compiled service (<c>TypeCacheCommandDiscovery</c>)
    ///     registers a <c>TypeCache</c>-backed provider through an
    ///     initialization hook so precompiled command assemblies resolve
    ///     exactly, without walking their types.
    /// </summary>
    /// <remarks>
    ///     Providers are domain-wide and single-threaded, like all shell
    ///     command state: register from Unity initialization hooks (or any
    ///     point before first use) and dispose the returned handle to remove.
    ///     The shell does not cache provider results; providers own their
    ///     caching. A provider that claims an assembly with zero entries
    ///     asserts that the assembly carries no commands.
    /// </remarks>
    internal interface ICommandDiscoveryProvider
    {
        /// <summary>
        ///     Collects auto-command candidates for one assembly. Returns true
        ///     to claim the assembly (reflection discovery is then skipped for
        ///     it) or false to pass it to the next provider and, failing that,
        ///     the reflection compatibility walk. Entries are appended to the
        ///     caller-owned <paramref name="commands"/> buffer; appends survive
        ///     whether the assembly is claimed or passed, and a throwing
        ///     provider's partial appends are rolled back and treated as a
        ///     pass.
        /// </summary>
        bool TryCollect(Assembly assembly, List<CommandShell.AutoCommand> commands);
    }
}
