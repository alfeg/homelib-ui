type BuildInfo = {
    repository: string
    ref: string
    sha: string
    shortSha: string
    runId: string
    timestamp: string
    sourceUrl: string
}

function readEnv(name: string): string {
    const value = (import.meta.env[name] as string | undefined) ?? ""
    return value.trim()
}

const repository = readEnv("VITE_BUILD_REPOSITORY")
const ref = readEnv("VITE_BUILD_REF")
const sha = readEnv("VITE_BUILD_SHA")
const runId = readEnv("VITE_BUILD_RUN_ID")
const timestamp = readEnv("VITE_BUILD_TIMESTAMP")

const shortSha = sha ? sha.slice(0, 12) : ""
const sourceUrl = repository && sha ? `https://github.com/${repository}/commit/${sha}` : ""

export const buildInfo: BuildInfo = {
    repository,
    ref,
    sha,
    shortSha,
    runId,
    timestamp,
    sourceUrl,
}

export function buildInfoLine(): string {
    const parts: string[] = []

    if (buildInfo.repository) parts.push(buildInfo.repository)
    if (buildInfo.ref) parts.push(buildInfo.ref)
    if (buildInfo.shortSha) parts.push(buildInfo.shortSha)

    return parts.join(" @ ")
}
