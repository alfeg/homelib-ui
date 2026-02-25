const UTF8_DECODER = new TextDecoder("utf-8")

function createParseError(message: string) {
    return new Error(`Invalid .torrent file: ${message}`)
}

function parseByteString(bytes: Uint8Array, offset: number) {
    const start = offset

    while (offset < bytes.length && bytes[offset] >= 48 && bytes[offset] <= 57) {
        offset += 1
    }

    if (offset === start || offset >= bytes.length || bytes[offset] !== 58) {
        throw createParseError("failed to read bencode byte string length.")
    }

    const lengthText = String.fromCharCode(...bytes.slice(start, offset))
    const length = Number.parseInt(lengthText, 10)

    if (!Number.isFinite(length) || length < 0) {
        throw createParseError("byte string length is invalid.")
    }

    const valueStart = offset + 1
    const valueEnd = valueStart + length
    if (valueEnd > bytes.length) {
        throw createParseError("byte string exceeds file size.")
    }

    return {
        value: bytes.slice(valueStart, valueEnd),
        nextOffset: valueEnd,
    }
}

function parseInteger(bytes: Uint8Array, offset: number) {
    if (bytes[offset] !== 105) {
        throw createParseError("failed to parse integer value.")
    }

    const end = bytes.indexOf(101, offset + 1)
    if (end < 0) {
        throw createParseError("unterminated integer value.")
    }

    const digits = String.fromCharCode(...bytes.slice(offset + 1, end))
    if (!/^[-]?\d+$/.test(digits)) {
        throw createParseError("integer token is invalid.")
    }

    return {
        value: Number.parseInt(digits, 10),
        nextOffset: end + 1,
    }
}

function decodeUtf8(byteString) {
    if (!(byteString instanceof Uint8Array)) {
        return ""
    }

    return UTF8_DECODER.decode(byteString).trim()
}

function parseValue(bytes: Uint8Array, offset: number): { value: any; nextOffset: number } {
    if (offset >= bytes.length) {
        throw createParseError("unexpected end of file.")
    }

    const token = bytes[offset]

    if (token >= 48 && token <= 57) {
        return parseByteString(bytes, offset)
    }

    if (token === 105) {
        return parseInteger(bytes, offset)
    }

    if (token === 108) {
        const items = []
        let cursor = offset + 1

        while (cursor < bytes.length && bytes[cursor] !== 101) {
            const parsed = parseValue(bytes, cursor)
            items.push(parsed.value)
            cursor = parsed.nextOffset
        }

        if (cursor >= bytes.length || bytes[cursor] !== 101) {
            throw createParseError("unterminated list value.")
        }

        return {
            value: items,
            nextOffset: cursor + 1,
        }
    }

    if (token === 100) {
        const dictionary = {}
        let cursor = offset + 1

        while (cursor < bytes.length && bytes[cursor] !== 101) {
            const key = parseByteString(bytes, cursor)
            const keyText = decodeUtf8(key.value)
            cursor = key.nextOffset

            const parsed = parseValue(bytes, cursor)
            dictionary[keyText] = parsed.value
            cursor = parsed.nextOffset
        }

        if (cursor >= bytes.length || bytes[cursor] !== 101) {
            throw createParseError("unterminated dictionary value.")
        }

        return {
            value: dictionary,
            nextOffset: cursor + 1,
        }
    }

    throw createParseError("contains an unsupported bencode token.")
}

function extractTrackers(announceValue: unknown, announceListValue: unknown) {
    const trackers = []

    const addTracker = (value) => {
        const tracker = decodeUtf8(value)
        if (!tracker || trackers.includes(tracker)) {
            return
        }

        trackers.push(tracker)
    }

    if (announceValue instanceof Uint8Array) {
        addTracker(announceValue)
    }

    const walk = (value) => {
        if (value instanceof Uint8Array) {
            addTracker(value)
            return
        }

        if (Array.isArray(value)) {
            value.forEach(walk)
        }
    }

    walk(announceListValue)
    return trackers
}

function parseTorrentMetadata(bytes: Uint8Array) {
    if (!(bytes instanceof Uint8Array) || !bytes.length) {
        throw createParseError("file is empty.")
    }

    if (bytes[0] !== 100) {
        throw createParseError("torrent root value must be a dictionary.")
    }

    let cursor = 1
    let infoStart = -1
    let infoEnd = -1
    let infoValue = null
    let announceValue = null
    let announceListValue = null

    while (cursor < bytes.length && bytes[cursor] !== 101) {
        const key = parseByteString(bytes, cursor)
        const keyText = decodeUtf8(key.value)
        cursor = key.nextOffset

        if (!keyText) {
            throw createParseError("contains an empty dictionary key.")
        }

        if (keyText === "info") {
            infoStart = cursor
            const parsed = parseValue(bytes, cursor)
            infoValue = parsed.value
            cursor = parsed.nextOffset
            infoEnd = cursor
            continue
        }

        const parsed = parseValue(bytes, cursor)
        cursor = parsed.nextOffset

        if (keyText === "announce") {
            announceValue = parsed.value
            continue
        }

        if (keyText === "announce-list") {
            announceListValue = parsed.value
        }
    }

    if (cursor >= bytes.length || bytes[cursor] !== 101) {
        throw createParseError("torrent root dictionary is not terminated.")
    }

    if (cursor !== bytes.length - 1) {
        throw createParseError("contains unexpected trailing data.")
    }

    if (infoStart < 0 || infoEnd <= infoStart) {
        throw createParseError("missing required info dictionary.")
    }

    const infoDict = infoValue && typeof infoValue === "object" ? infoValue : {}
    const displayName = decodeUtf8(
        infoDict["name.utf-8"] instanceof Uint8Array ? infoDict["name.utf-8"] : infoDict.name,
    )
    const trackers = extractTrackers(announceValue, announceListValue)

    return {
        infoBytes: bytes.slice(infoStart, infoEnd),
        displayName,
        trackers,
    }
}

function bytesToHex(bytes: Uint8Array) {
    return Array.from(bytes, (byte) => byte.toString(16).padStart(2, "0")).join("")
}

function buildMagnetUri(hash: string, metadata: { displayName: string; trackers: string[] }) {
    const params = new URLSearchParams()
    params.set("xt", `urn:btih:${hash}`)

    if (metadata.displayName) {
        params.set("dn", metadata.displayName)
    }

    metadata.trackers.forEach((tracker) => {
        params.append("tr", tracker)
    })

    return `magnet:?${params.toString()}`
}

export async function convertTorrentFileToMagnet(file: File | Blob) {
    if (!(file instanceof Blob)) {
        throw new Error("Please choose a .torrent file.")
    }

    if (!crypto?.subtle) {
        throw new Error("This browser does not support torrent conversion.")
    }

    try {
        const bytes = new Uint8Array(await file.arrayBuffer())
        const metadata = parseTorrentMetadata(bytes)
        const digest = await crypto.subtle.digest("SHA-1", metadata.infoBytes)
        const hash = bytesToHex(new Uint8Array(digest))
        return buildMagnetUri(hash, metadata)
    } catch (err) {
        if (err instanceof Error) {
            throw err
        }

        throw new Error("Failed to parse .torrent file.")
    }
}
