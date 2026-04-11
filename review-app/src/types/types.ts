export interface MediaDto {
    externalApiID: string;
    mediaType: number; // 0 = Movie, 1 = Series, 2 = Music
    title: string;
    releaseDate?: string;
    posterUrl?: string;
    overview?: string;
    creator?: string;
}