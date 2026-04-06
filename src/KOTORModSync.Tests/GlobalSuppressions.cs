// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: NUnit.Framework.Timeout(120_000)]

[assembly: SuppressMessage("Usage", "MA0004:Use Task.ConfigureAwait(false)", Justification = "<Pending>", Scope = "member", Target = "~M:KOTORModSync.Tests.VirtualFileSystemWildcardTests.RunBothProviders(System.Collections.Generic.List{KOTORModSync.Core.Instruction},System.String,System.String)~System.Threading.Tasks.Task{System.ValueTuple{KOTORModSync.Core.Services.FileSystem.VirtualFileSystemProvider,KOTORModSync.Core.Services.FileSystem.RealFileSystemProvider}}")]
