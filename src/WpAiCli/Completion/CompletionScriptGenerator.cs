namespace WpAiCli.Completion;

public static class CompletionScriptGenerator
{
    private static readonly string[] Shells = { "bash", "zsh", "powershell" };

    public static IReadOnlyList<string> SupportedShells => Shells;

    public static string Generate(string shell)
    {
        return shell.ToLowerInvariant() switch
        {
            "bash" => GetBashScript(),
            "zsh" => GetZshScript(),
            "powershell" => GetPowerShellScript(),
            _ => throw new ArgumentException($"未対応のシェルです: {shell}", nameof(shell))
        };
    }

    private static string GetBashScript() => """
# wpai bash completion
_wpai()
{
    local cur prev words cword
    _init_completion -n : || return

    local root_cmds="posts categories tags completion docs"
    local global_opts="--format --help --version"

    if [[ ${cword} -eq 1 ]]; then
        COMPREPLY=( $(compgen -W "$root_cmds" -- "$cur") )
        return 0
    fi

    case "${words[1]}" in
        posts)
            local post_sub="list get create update delete"
            if [[ ${cword} -eq 2 ]]; then
                COMPREPLY=( $(compgen -W "$post_sub" -- "$cur") )
                return 0
            fi
            case "${words[2]}" in
                list)
                    COMPREPLY=( $(compgen -W "--status --per-page --page $global_opts" -- "$cur") )
                    ;;
                get)
                    COMPREPLY=( $(compgen -W "--id $global_opts" -- "$cur") )
                    ;;
                create)
                    COMPREPLY=( $(compgen -W "--title --content --content-file --status --categories --tags --featured-media $global_opts" -- "$cur") )
                    ;;
                update)
                    COMPREPLY=( $(compgen -W "--id --title --content --content-file --status --categories --tags --featured-media $global_opts" -- "$cur") )
                    ;;
                delete)
                    COMPREPLY=( $(compgen -W "--id --force $global_opts" -- "$cur") )
                    ;;
            esac
            ;;
        categories)
            COMPREPLY=( $(compgen -W "list $global_opts" -- "$cur") )
            ;;
        tags)
            COMPREPLY=( $(compgen -W "list $global_opts" -- "$cur") )
            ;;
        completion)
            COMPREPLY=( $(compgen -W "--shell" -- "$cur") )
            ;;
        docs)
            COMPREPLY=()
            ;;
        *)
            COMPREPLY=( $(compgen -W "$global_opts" -- "$cur") )
            ;;
    esac
}
complete -F _wpai wpai
""";

    private static string GetZshScript() => """
#compdef wpai
local -a root_cmds=(posts categories tags completion docs)
local -a global_opts=(--format --help --version)

_wpai() {
  local context state state_descr line
  typeset -A opt_args

  _arguments -C \
    '1:command:((posts:"Manage posts" categories:"List categories" tags:"List tags" completion:"Generate completion script" docs:"Show embedded docs"))' \
    '*::subcmd:->subcmd'

  case $state in
    subcmd)
      case $words[2] in
        posts)
          _arguments \
            '2:post command:(list get create update delete)' \
            '*::options:->postsopts'
          case $words[3] in
            list)
              _values 'options' --status --per-page --page $global_opts
              ;;
            get)
              _values 'options' --id $global_opts
              ;;
            create)
              _values 'options' --title --content --content-file --status --categories --tags --featured-media $global_opts
              ;;
            update)
              _values 'options' --id --title --content --content-file --status --categories --tags --featured-media $global_opts
              ;;
            delete)
              _values 'options' --id --force $global_opts
              ;;
          esac
          ;;
        categories|tags)
          _values 'sub commands' list
          ;;
        completion)
          _values 'options' --shell
          ;;
      esac
      ;;
  esac
}

_wpai "$@"
""";

    private static string GetPowerShellScript() => """
# wpai PowerShell completion
Register-ArgumentCompleter -Native -CommandName wpai -ScriptBlock {
    param($wordToComplete, $commandAst, $cursorPosition)

    $commands = @('posts','categories','tags','completion','docs')
    $global = @('--format','--help','--version')

    $tokens = $commandAst.CommandElements | ForEach-Object { $_.ToString() }
    if ($tokens.Count -le 1)
    {
        $commands | Where-Object { $_ -like "${wordToComplete}*" } | ForEach-Object {
            [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
        }
        return
    }

    switch ($tokens[1])
    {
        'posts' {
            if ($tokens.Count -eq 2)
            {
                foreach ($item in @('list','get','create','update','delete'))
                {
                    if ($item -like "${wordToComplete}*")
                    {
                        [System.Management.Automation.CompletionResult]::new($item, $item, 'ParameterValue', $item)
                    }
                }
                return
            }

            $options = @('--status','--per-page','--page','--id','--title','--content','--content-file','--status','--categories','--tags','--featured-media','--force') + $global
            $options | Sort-Object -Unique | Where-Object { $_ -like "${wordToComplete}*" } | ForEach-Object {
                [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterName', $_)
            }
            return
        }
        'categories' {
            foreach ($item in @('list'))
            {
                if ($item -like "${wordToComplete}*")
                {
                    [System.Management.Automation.CompletionResult]::new($item, $item, 'ParameterValue', $item)
                }
            }
            return
        }
        'tags' {
            foreach ($item in @('list'))
            {
                if ($item -like "${wordToComplete}*")
                {
                    [System.Management.Automation.CompletionResult]::new($item, $item, 'ParameterValue', $item)
                }
            }
            return
        }
        'completion' {
            foreach ($item in @('--shell'))
            {
                if ($item -like "${wordToComplete}*")
                {
                    [System.Management.Automation.CompletionResult]::new($item, $item, 'ParameterName', $item)
                }
            }
            return
        }
    }

    $global | Where-Object { $_ -like "${wordToComplete}*" } | ForEach-Object {
        [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterName', $_)
    }
}
""";
}
