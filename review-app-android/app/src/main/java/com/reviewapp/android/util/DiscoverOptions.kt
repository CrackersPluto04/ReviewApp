package com.reviewapp.android.util

import com.reviewapp.android.domain.model.MediaType

/** A value sent to the API with the label shown for it. */
data class Option(val value: String, val label: String)

/** Filter values of the discover and review lists, the same as on the web app. */
object DiscoverOptions {
    const val DEFAULT_TMDB_SORT = "popularity.desc"
    const val DEFAULT_MUSIC_GENRE = "pop"
    const val DEFAULT_MARKET = "US"

    fun tmdbSortOptions(type: MediaType): List<Option> {
        val dateField = if (type == MediaType.Series) "first_air_date" else "primary_release_date"
        val nameField = if (type == MediaType.Series) "name" else "title"
        return listOf(
            Option("popularity.desc", "Most Popular"),
            Option("popularity.asc", "Least Popular"),
            Option("$dateField.desc", "Newest Releases"),
            Option("$dateField.asc", "Oldest Releases"),
            Option("$nameField.asc", "A-Z"),
            Option("$nameField.desc", "Z-A"),
        )
    }

    fun tmdbGenres(type: MediaType): List<Option> =
        if (type == MediaType.Series) seriesGenres else movieGenres

    private val movieGenres = listOf(
        Option("28", "Action"), Option("12", "Adventure"), Option("16", "Animation"),
        Option("35", "Comedy"), Option("80", "Crime"), Option("99", "Documentary"),
        Option("18", "Drama"), Option("10751", "Family"), Option("14", "Fantasy"),
        Option("36", "History"), Option("27", "Horror"), Option("10402", "Music"),
        Option("9648", "Mystery"), Option("10749", "Romance"), Option("878", "Sci-Fi"),
        Option("10770", "TV Movie"), Option("53", "Thriller"), Option("10752", "War"),
        Option("37", "Western"),
    )

    private val seriesGenres = listOf(
        Option("10759", "Action & Adventure"), Option("16", "Animation"), Option("35", "Comedy"),
        Option("80", "Crime"), Option("99", "Documentary"), Option("18", "Drama"),
        Option("10751", "Family"), Option("10762", "Kids"), Option("9648", "Mystery"),
        Option("10763", "News"), Option("10764", "Reality"), Option("10765", "Sci-Fi & Fantasy"),
        Option("10766", "Soap"), Option("10767", "Talk"), Option("10768", "War & Politics"),
        Option("37", "Western"),
    )

    val musicGenres: List<Option> = listOf(
        "acoustic", "afrobeat", "alt-rock", "alternative", "ambient", "anime", "classical",
        "country", "dance", "edm", "hip-hop", "indie", "jazz", "k-pop", "metal", "pop", "r-n-b",
        "rock", "synth-pop", "techno",
    ).map { Option(it, it.replaceFirstChar(Char::uppercase)) }

    val markets: List<Option> = listOf(
        "US", "GB", "CA", "DE", "FR", "IT", "ES", "AU", "BR", "MX", "JP", "KR", "SE", "NL", "RU",
        "IN", "ZA", "AR", "BE", "CH", "HU", "RO", "PL", "AT", "GR",
    ).map { Option(it, it) }

    const val DEFAULT_REVIEW_SORT = "created_desc"

    val reviewSortOptions: List<Option> = listOf(
        Option("created_desc", "Newest First"),
        Option("created_asc", "Oldest First"),
        Option("updated_desc", "Recently Updated"),
        Option("updated_asc", "Least Recently Updated"),
        Option("score_desc", "Highest Score"),
        Option("score_asc", "Lowest Score"),
    )

    /** Same default as the web's collections tab, so Favourites (the first one made) comes first. */
    const val DEFAULT_COLLECTION_SORT = "createdAt_asc"

    val collectionSortOptions: List<Option> = listOf(
        Option("createdAt_asc", "Oldest First"),
        Option("createdAt_desc", "Newest First"),
        Option("name_asc", "Name A-Z"),
        Option("name_desc", "Name Z-A"),
    )
}
