#!/usr/bin/env bash
# Shared, private restore environment. Caller supplies a newly created trusted directory.
agd_isolate_nuget() {
    local root="$1" key
    AGD_DOTNET_HOST="$(type -P dotnet)"
    [[ "$AGD_DOTNET_HOST" == /* && -f "$AGD_DOTNET_HOST" && -x "$AGD_DOTNET_HOST" ]] || return 1
    AGD_DOTNET_HOST="$(readlink -f -- "$AGD_DOTNET_HOST")"
    AGD_CLEAN_ROOT="$root"
    while IFS= read -r key; do
        case "${key,,}" in
            restore*|nuget*|msbuild*|dotnet_msbuild_sdk_resolver_*) unset "$key" ;;
        esac
    done < <(compgen -e)
    mkdir "$root/home" "$root/packages" "$root/http-cache" "$root/scratch"
    export DOTNET_CLI_HOME="$root/home"
    export NUGET_PACKAGES="$root/packages"
    export NUGET_HTTP_CACHE_PATH="$root/http-cache"
    export NUGET_SCRATCH="$root/scratch"
    export MSBUILDDISABLENODEREUSE=1
    export DOTNET_CLI_USE_MSBUILD_SERVER=0
    export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
    export DOTNET_CLI_TELEMETRY_OPTOUT=1
    export DOTNET_NOLOGO=1
}

# Installation/evaluation/build/shim processes receive no inherited CLR hooks,
# profilers, external MSBuild imports, runtime/dependency overrides or credentials.
# The installed .NET host and SDK are the explicitly trusted host prerequisite.
agd_clean_run() {
    /usr/bin/env -i PATH="$(dirname "$AGD_DOTNET_HOST"):/usr/bin:/bin" \
        HOME="$AGD_CLEAN_ROOT/home" DOTNET_CLI_HOME="$AGD_CLEAN_ROOT/home" \
        DOTNET_ROOT="$(dirname "$AGD_DOTNET_HOST")" \
        NUGET_PACKAGES="$AGD_CLEAN_ROOT/packages" NUGET_HTTP_CACHE_PATH="$AGD_CLEAN_ROOT/http-cache" \
        NUGET_SCRATCH="$AGD_CLEAN_ROOT/scratch" \
        MSBuildUserExtensionsPath="$AGD_CLEAN_ROOT/home/extensions" \
        MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 \
        DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 \
        LANG=C.UTF-8 LC_ALL=C.UTF-8 "$@"
}
