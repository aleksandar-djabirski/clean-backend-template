using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

var product = Path.GetFullPath(args.Length >= 2 && args[0] == "--product" ? args[1] : ".");
var name = new DirectoryInfo(product).Name;
var problems = new List<string>();
var apiProject = Path.Combine(product, "src", $"{name}.Api", $"{name}.Api.csproj");
var absProject = Path.Combine(product, "src", $"{name}.Abstractions", $"{name}.Abstractions.csproj");
Require("AGENTS.md"); Require("README.md"); Require("ARCHITECTURE.md"); Require("eng/package-policy.json"); Require("eng/guardrail-exceptions.json"); Require("docs/ownership-map.md"); Require("docs/guardrail-exceptions.md");
if (!File.Exists(apiProject)) problems.Add($"Missing API project: {apiProject}");
if (!File.Exists(absProject)) problems.Add($"Missing abstractions project: {absProject}");
if (File.Exists(apiProject))
{
    var text = File.ReadAllText(apiProject);
    if (!text.Contains("Include=\"CleanBackend.Guardrails.Analyzers\"", StringComparison.Ordinal)) problems.Add("API project must reference CleanBackend.Guardrails.Analyzers.");
    if (!text.Contains($"{name}.Abstractions.csproj", StringComparison.Ordinal)) problems.Add($"API project must reference {name}.Abstractions.");
    if (!text.Contains("RestorePackagesWithLockFile", StringComparison.Ordinal)) problems.Add("API project must enable RestorePackagesWithLockFile.");
}
if (!File.Exists(Path.Combine(product, "src", $"{name}.Api", "packages.lock.json"))) problems.Add("Missing API packages.lock.json.");
VerifyModuleBoundaries(); VerifyPackagePolicy(); VerifyGuardrailExceptions(); VerifyPlaceholders();
if (problems.Count > 0) { foreach (var p in problems) Console.Error.WriteLine(p); return 1; }
var psi = new ProcessStartInfo("dotnet", $"build \"{apiProject}\" -p:RestoreLockedMode=true --nologo") { WorkingDirectory = product };
var process = Process.Start(psi)!; process.WaitForExit(); return process.ExitCode;
void Require(string relative) { if (!File.Exists(Path.Combine(product, relative))) problems.Add($"Missing required file: {relative}"); }
void VerifyModuleBoundaries(){ var modules=Path.Combine(product,"src",$"{name}.Api","Modules"); if(!Directory.Exists(modules)){problems.Add($"Missing modules directory: {modules}"); return;} foreach(var dir in Directory.GetDirectories(modules)){var m=Path.GetFileName(dir); if(!File.Exists(Path.Combine(dir,$"{m}Module.cs"))) problems.Add($"Missing module facade: {m}"); if(!File.Exists(Path.Combine(product,"docs","modules",$"{m}.capabilities.md"))) problems.Add($"Missing module capability map: {m}");} var rx=new Regex(@"(?<modifiers>(?:\b(?:public|internal|sealed|static|abstract|partial|file|readonly|ref)\b\s+)*)\b(?:class|struct|interface|enum|record|delegate)\b"); foreach(var f in Directory.EnumerateFiles(modules,"*.cs",SearchOption.AllDirectories).Where(f=>!f.EndsWith("Module.cs",StringComparison.Ordinal))){ if(rx.Matches(Strip(File.ReadAllText(f))).Any(m=>Regex.IsMatch(m.Groups["modifiers"].Value,@"\bpublic\b"))) problems.Add($"Module implementation types must be internal: {f}");}}
void VerifyPackagePolicy(){ var p=Path.Combine(product,"eng","package-policy.json"); if(!File.Exists(p)) return; using var doc=JsonDocument.Parse(File.ReadAllText(p)); if(doc.RootElement.GetProperty("lockedRestoreRequired").GetBoolean() && !File.Exists(Path.Combine(product,"src",$"{name}.Api","packages.lock.json"))) problems.Add("Locked restore required but lock file is missing."); if(doc.RootElement.TryGetProperty("prohibitedPackages",out var pkgs)){ foreach(var pkg in pkgs.EnumerateArray().Select(x=>x.GetString()).Where(x=>x is not null)){ foreach(var csproj in Directory.EnumerateFiles(Path.Combine(product,"src"),"*.csproj",SearchOption.AllDirectories)){ if(File.ReadAllText(csproj).Contains($"Include=\"{pkg}\"",StringComparison.OrdinalIgnoreCase)) problems.Add($"Prohibited package '{pkg}' is referenced in {csproj}.");}}}}
void VerifyGuardrailExceptions(){ var p=Path.Combine(product,"eng","guardrail-exceptions.json"); if(!File.Exists(p)) return; using var doc=JsonDocument.Parse(File.ReadAllText(p)); if(!doc.RootElement.TryGetProperty("exceptions",out var ex)||ex.ValueKind!=JsonValueKind.Array){problems.Add("guardrail-exceptions.json must contain exceptions array."); return;} var i=0; foreach(var e in ex.EnumerateArray()){var label=$"guardrail exception #{i++}"; foreach(var field in new[]{"rule","module","reason"}) if(!e.TryGetProperty(field,out var v)||v.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(v.GetString())) problems.Add($"{label} missing {field}."); if(!e.TryGetProperty("tests",out var t)||t.ValueKind!=JsonValueKind.Array||!t.EnumerateArray().Any()) problems.Add($"{label} must list tests."); if(!e.TryGetProperty("expires",out var x)||!DateOnly.TryParse(x.GetString(),out var d)) problems.Add($"{label} must set expires."); else if(d<DateOnly.FromDateTime(DateTime.UtcNow)) problems.Add($"{label} expired on {d:yyyy-MM-dd}.");}}
void VerifyPlaceholders(){ foreach(var f in Directory.EnumerateFiles(product,"*",SearchOption.AllDirectories).Where(f=>!f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")&&!f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")&&!f.Contains($"{Path.DirectorySeparatorChar}local-feed{Path.DirectorySeparatorChar}"))){ var t=File.ReadAllText(f); if(t.Contains("{{",StringComparison.Ordinal)||t.Contains("TODO_TEMPLATE",StringComparison.Ordinal)) problems.Add($"Unresolved placeholder: {f}");}}
static string Strip(string s)=>Regex.Replace(Regex.Replace(s,@"//.*|/\*[\s\S]*?\*/",""),@"@?""(""""|[^""])*""|'(\\.|[^'])'","");
