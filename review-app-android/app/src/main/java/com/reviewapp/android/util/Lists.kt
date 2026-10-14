package com.reviewapp.android.util

/** A copy with the item at [from] taken out and put back at [to], the others shift (like the API's reorder). */
fun <T> List<T>.moved(from: Int, to: Int): List<T> {
    if (from == to || from !in indices || to !in indices) return this
    return toMutableList().apply { add(to, removeAt(from)) }
}
