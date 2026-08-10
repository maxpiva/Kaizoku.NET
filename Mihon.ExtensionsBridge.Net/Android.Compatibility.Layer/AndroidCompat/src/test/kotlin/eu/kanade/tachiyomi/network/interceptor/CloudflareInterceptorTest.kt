package eu.kanade.tachiyomi.network.interceptor

import okhttp3.FormBody
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class CloudflareInterceptorTest {
    @Test
    fun `form post is forwarded through request post`() {
        val request =
            Request
                .Builder()
                .url("https://example.com/search?page=2")
                .post(FormBody.Builder().add("query", "one piece").build())
                .build()

        val target = with(CFClearance) { request.toFlareSolverTarget() }

        assertEquals("request.post", target.cmd)
        assertEquals("https://example.com/search?page=2", target.url)
        assertEquals("query=one+piece", target.postData)
        assertEquals(true, target.canUseResponseFallback)
    }

    @Test
    fun `json post solves at origin and leaves body for okhttp retry`() {
        val jsonBody = """{"title":"one piece"}""".toRequestBody("application/json".toMediaType())
        val request =
            Request
                .Builder()
                .url("https://kagane.to/api/v2/search/series?page=0&size=35")
                .post(jsonBody)
                .build()

        val target = with(CFClearance) { request.toFlareSolverTarget() }

        assertEquals("request.get", target.cmd)
        assertEquals("https://kagane.to/", target.url)
        assertNull(target.postData)
        assertEquals(false, target.canUseResponseFallback)
        assertEquals(jsonBody, request.body)
        assertEquals("application/json; charset=utf-8", request.body?.contentType().toString())
    }

    @Test
    fun `get keeps its original target`() {
        val request = Request.Builder().url("https://example.com/path?q=value").build()

        val target = with(CFClearance) { request.toFlareSolverTarget() }

        assertEquals("request.get", target.cmd)
        assertEquals("https://example.com/path?q=value", target.url)
        assertNull(target.postData)
        assertEquals(true, target.canUseResponseFallback)
    }
}
