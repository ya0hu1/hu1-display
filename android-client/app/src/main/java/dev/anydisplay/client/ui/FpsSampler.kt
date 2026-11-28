package dev.anydisplay.client.ui

/** One active decoder/surface per sampling window. No estimate across a pause or decoder swap. */
internal class FpsSampler {
    private var time = -1L
    private var frames = 0L
    fun reset() { time = -1 }
    fun sample(nowNs: Long, count: Long): Double? {
        val rate = if (time >= 0 && nowNs > time && count >= frames)
            (count - frames) * 1e9 / (nowNs - time) else null
        time = nowNs
        frames = count
        return rate
    }
}
