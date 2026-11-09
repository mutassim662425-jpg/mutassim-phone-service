// Explicit global usings for the WPF project (the WindowsDesktop SDK trims some implicit usings
// to avoid System.IO.Path vs System.Windows.Shapes.Path ambiguity; we never import Shapes in code).
global using System.IO;
global using System.Net.Http;
