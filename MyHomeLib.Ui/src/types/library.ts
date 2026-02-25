export interface LibraryMetadata {
    description?: string
    version?: string
    totalBooks?: number
}
export interface BookRecord {
    id: number | string
    title: string
    genre: string
    genreCodes: string[]
    authors: string
    series: string
    seriesNo: string
    lang: string
    file: string
    ext: string
    archiveFile: string
    date?: string
}
export interface ProgressState {
    phase: string
    processed: number
    total: number
    percent: number
    downloadedBytes: number
    totalBytes: number | null
}
