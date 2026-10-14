package com.reviewapp.android.data.remote

import kotlinx.serialization.json.Json

/** JSON settings for talking to the API. */
val ApiJson = Json {
    ignoreUnknownKeys = true
    coerceInputValues = true
    // Nulls are left out of requests, the API treats a missing property as null
    explicitNulls = false
    // Values equal to the Kotlin default must still be sent: the C# DTOs have their own defaults
    // (e.g. a missing visibilityLevel would become Private instead of Public)
    encodeDefaults = true
}
