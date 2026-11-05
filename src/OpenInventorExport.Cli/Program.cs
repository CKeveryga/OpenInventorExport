// No arguments: interactive menu. With arguments: one-shot command (for scripts).
return args.Length == 0 ? Interactive.Run() : Cli.Run(args);
