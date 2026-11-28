package dev.anydisplay.client.ui

import dev.anydisplay.client.data.HudMode
import dev.anydisplay.client.data.StreamPolicy
import org.junit.Assert.*
import org.junit.Test

class StreamPolicyTest {
    @Test fun manualTargetSurvivesSelectingPerformance() {
        assertEquals(30, StreamPolicy.requestHz(30, 90, false))
        assertEquals(90, StreamPolicy.requestHz(30, 90, true))
        assertEquals(30, StreamPolicy.requestHz(30, 90, false))
    }
    @Test fun targetsStayWithinPanelAndProtocolPolicy() {
        assertEquals(60, StreamPolicy.requestHz(120, 60, false))
        assertEquals(60, StreamPolicy.requestHz(120, 60, true))
        assertEquals(120, StreamPolicy.requestHz(60, 144, true))
    }
    @Test fun hudIsOptInEvenForUnknownStoredValues() {
        assertEquals(HudMode.OFF, HudMode.from(null))
        assertEquals(HudMode.OFF, HudMode.from("unknown"))
        HudMode.entries.forEach { assertEquals(it, HudMode.from(it.key)) }
    }
    @Test fun outputSampleUsesElapsedTimeAndResetsAcrossSurfaces() {
        val s = FpsSampler()
        assertNull(s.sample(0, 100))
        assertEquals(80.0, s.sample(1_500_000_000, 220)!!, 0.0001)
        assertEquals(0.0, s.sample(2_500_000_000, 220)!!, 0.0)
        s.reset()
        assertNull(s.sample(8_000_000_000, 220))
        assertNull(s.sample(9_000_000_000, 0))
    }
}
