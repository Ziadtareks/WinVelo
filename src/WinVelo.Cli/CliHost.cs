using System.Text;

namespace WinVelo.Cli;

public static class CliHost
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    { ["-h"]="--help", ["-y"]="--yes", ["-p"]="--preset", ["-l"]="--level", ["--adapter-index"]="--adapter" };
    private static readonly HashSet<string> Values = new(StringComparer.OrdinalIgnoreCase)
    { "--catalog","--info","--diagnostics","--recover","--level","--preset","--tweak","--tweaks","--feature","--clean","--dns","--update-mode","--fix","--config","--adapter" };
    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase)
    { "--help","--interactive","--self-test","--test-mode","--detect-all","--real-mode","--dry-run","--run","--apply","--enable","--disable","--undo","--health-check","--restore","--list","--yes","--yes-irreversible","--dns-status","--doh" };

    public static string[] Normalize(string[] args)
    {
        var result = new List<string>(); var seen = new HashSet<string>();
        for (int i=0;i<args.Length;i++)
        {
            string token=args[i]; int equals=token.IndexOf('=');
            string option=(equals<0?token:token[..equals]).ToLowerInvariant();
            if (Aliases.TryGetValue(option,out var alias)) option=alias;
            if (!Values.Contains(option) && !Flags.Contains(option)) throw new ArgumentException("Unknown option: " + token + ". Use --help.");
            if (!seen.Add(option)) throw new ArgumentException("Duplicate option: " + option);
            result.Add(option);
            if (Values.Contains(option))
            {
                string value=equals>=0?token[(equals+1)..] : ++i<args.Length?args[i]:"";
                if (string.IsNullOrWhiteSpace(value) || value.StartsWith("--") || Aliases.ContainsKey(value)) throw new ArgumentException("Missing value for " + option);
                result.Add(value);
            }
            else if (equals>=0) throw new ArgumentException("This option does not accept a value: " + option);
        }
        if (seen.Contains("--help")) return result.ToArray();
        if (seen.Contains("--real-mode") && seen.Contains("--test-mode")) throw new ArgumentException("Choose either --real-mode or --test-mode.");
        if (seen.Contains("--apply") && seen.Contains("--undo") || seen.Contains("--enable") && seen.Contains("--disable")) throw new ArgumentException("Conflicting action options.");
        string[] selectors={"--list","--info","--diagnostics","--self-test","--detect-all","--health-check","--preset","--tweak","--tweaks","--feature","--clean","--dns","--update-mode","--fix","--config","--dns-status"};
        if (selectors.Count(seen.Contains)>1) throw new ArgumentException("Choose one command per invocation.");
        if (seen.Contains("--interactive") && result.Count!=1) throw new ArgumentException("Start --interactive by itself, then enter individual commands.");
        if (seen.Contains("--adapter"))
        {
            if (!seen.Contains("--dns") && !seen.Contains("--dns-status")) throw new ArgumentException("--adapter requires --dns or --dns-status.");
            string value=result[result.IndexOf("--adapter")+1];
            if (!int.TryParse(value,out int index) || index<=0) throw new ArgumentException("Adapter index must be a positive integer.");
        }
        if (seen.Contains("--doh") && !seen.Contains("--dns")) throw new ArgumentException("--doh requires --dns.");
        if (seen.Contains("--undo") && !seen.Contains("--tweak") && !seen.Contains("--tweaks")) throw new ArgumentException("--undo requires --tweak or --tweaks.");
        if (seen.Contains("--restore") && !seen.Contains("--health-check")) throw new ArgumentException("--restore requires --health-check.");
        if ((seen.Contains("--enable") || seen.Contains("--disable")) && !seen.Contains("--feature")) throw new ArgumentException("--enable and --disable require --feature.");
        if (seen.Contains("--level"))
        {
            if (!seen.Contains("--self-test")) throw new ArgumentException("--level requires --self-test.");
            if (result[result.IndexOf("--level")+1] != "1") throw new ArgumentException("Supported self-test level: 1.");
        }
        if (seen.Contains("--self-test") && seen.Contains("--dry-run")) throw new ArgumentException("--self-test executes tests; use --test-mode for simulation.");
        if (result.Count>0 && !selectors.Any(seen.Contains) && !seen.Contains("--recover") && !seen.Contains("--dry-run") && !seen.Contains("--interactive")) throw new ArgumentException("No command selected. Use --help.");
        return result.ToArray();
    }

    public static string[] Split(string line)
    {
        var values=new List<string>(); var part=new StringBuilder(); char quote='\0'; bool started=false;
        for(int i=0;i<line.Length;i++)
        {
            char c=line[i];
            if(quote!='\0')
            {
                if(c==quote) { if(i+1<line.Length && line[i+1]==quote){part.Append(c);i++;}else quote='\0'; }
                else part.Append(c);
            }
            else if(c is '\'' or '"'){quote=c;started=true;}
            else if(char.IsWhiteSpace(c)){if(started){values.Add(part.ToString());part.Clear();started=false;}}
            else {part.Append(c);started=true;}
        }
        if(quote!='\0') throw new ArgumentException("Unclosed quote in command.");
        if(started) values.Add(part.ToString());
        return values.ToArray();
    }

    public static async Task<int> RunInteractiveAsync(TextReader input, TextWriter output, Func<string[],Task<int>> execute)
    {
        await output.WriteLineAsync("WinVelo interactive CLI. Enter --help for commands, or exit to close.");
        await output.WriteLineAsync("Commands use simulation by default; --real-mode explicitly selects the real system.");
        while(true)
        {
            await output.WriteAsync("winvelo> "); await output.FlushAsync();
            string? line=await input.ReadLineAsync(); if(line==null) return 0;
            if(string.IsNullOrWhiteSpace(line)) continue;
            if(line.Trim().Equals("exit",StringComparison.OrdinalIgnoreCase) || line.Trim().Equals("quit",StringComparison.OrdinalIgnoreCase)) return 0;
            try
            {
                string[] command=Split(line.Trim().Equals("help",StringComparison.OrdinalIgnoreCase)?"--help":line);
                if(command.Contains("--interactive",StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Interactive mode is already running.");
                int code=await execute(command);
                await output.WriteLineAsync($"[EXIT CODE] {code}");
            }
            catch(Exception ex){await output.WriteLineAsync("[ERROR] " + ex.Message);}
        }
    }
}
