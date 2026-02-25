const MAGNET_KEY = "myhomelib-magnet-uri"

function decodeBase32(value: string) {
    const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"
    const input = value.toUpperCase()
    let bits = ""

    for (const char of input) {
        const index = alphabet.indexOf(char)
        if (index < 0) throw new Error("Invalid base32 hash in magnet URI.")
        bits += index.toString(2).padStart(5, "0")
    }

    const bytes: number[] = []
    for (let i = 0; i + 8 <= bits.length; i += 8) {
        bytes.push(parseInt(bits.substring(i, i + 8), 2))
    }

    return bytes.map((b) => b.toString(16).padStart(2, "0")).join("")
}

export function parseHashFromMagnet(magnetUri: string) {
    if (!magnetUri?.startsWith("magnet:?")) {
        throw new Error("Please enter a valid magnet URI.")
    }

    const query = magnetUri.slice("magnet:?".length)
    const params = new URLSearchParams(query)
    const xtValues = params.getAll("xt")

    for (const xt of xtValues) {
        const normalized = xt.toLowerCase()
        if (!normalized.startsWith("urn:btih:")) continue

        const raw = xt.substring("urn:btih:".length).trim()
        if (/^[a-fA-F0-9]{40}$/.test(raw)) return raw.toLowerCase()
        if (/^[a-zA-Z2-7]{32}$/.test(raw)) return decodeBase32(raw)
        throw new Error("Magnet URI contains an unsupported torrent hash.")
    }

    throw new Error("Magnet URI does not contain a torrent hash.")
}

export const magnetStore = {
    get() {
        return localStorage.getItem(MAGNET_KEY) ?? ""
    },
    set(magnetUri: string) {
        localStorage.setItem(MAGNET_KEY, magnetUri)
    },
    clear() {
        localStorage.removeItem(MAGNET_KEY)
    },
}
