using System.Runtime.CompilerServices;

// The streaming answer parser is an implementation detail of GZLocalLLMBackend;
// exposing internals to the test assembly lets the EditMode suite exercise it
// without widening the public API.
[assembly: InternalsVisibleTo("GreatZimbabwe.Tests.Editor")]
