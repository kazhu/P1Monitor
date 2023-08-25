namespace P1Monitor.Tests;

[TestClass]
public class DsmrStringInternCacheTest
{
	[TestMethod]
	public void Test()
	{
		var cache = new DsmrStringInternCache(3);
		string cachedAbc = cache.Get("abc"u8);
		Assert.AreEqual("abc", cachedAbc);
		Assert.AreSame(cachedAbc, cache.Get("abc"u8));
		Assert.AreEqual("def", cache.Get("def"u8));
		Assert.AreSame(cachedAbc, cache.Get("abc"u8));
		Assert.AreEqual("!", cache.Get("!"u8));
		Assert.AreSame(cachedAbc, cache.Get("abc"u8));
		Assert.AreEqual("ghi", cache.Get("ghi"u8));
		Assert.AreNotSame(cachedAbc, cache.Get("abc"u8));
		Assert.AreEqual("jkl", cache.Get("jkl"u8));
		Assert.AreEqual("mno", cache.Get("mno"u8));
		Assert.AreEqual("pqr", cache.Get("pqr"u8));
		Assert.AreSame(cache.Get("pqr"u8), cache.Get("pqr"u8));
	}
}
