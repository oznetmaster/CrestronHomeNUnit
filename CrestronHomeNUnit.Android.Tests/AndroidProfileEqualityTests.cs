// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using NUnit.Framework;
namespace CrestronHomeNUnit.Android.Tests;

[TestFixture]
public sealed class AndroidProfileEqualityTests
{
 [TestCase(false)][TestCase(true)]
 public void IndependentJsonReadsPreserveProfileEquality(bool allowSelection) {
  var profile=new AndroidSessionProfile("adb","emulator","app","Home","lock") {
   AllowedStartingHomes=allowSelection?["Other Home"]:[] };
  var loaded=JsonSerializer.Deserialize<AndroidSessionProfile>(JsonSerializer.Serialize(profile))!;
  Assert.That(loaded==profile,Is.True);
  Assert.That(loaded.GetHashCode(),Is.EqualTo(profile.GetHashCode()));
  Assert.That(new HashSet<AndroidSessionProfile>{profile}.Contains(loaded),Is.True);
 }
 [Test] public void DifferentNavigationAuthorityOrEndpointRemainsUnequal() {
  var profile=new AndroidSessionProfile("adb","emulator","app","Home","lock") {AllowedStartingHomes=["Other"]};
  Assert.That(profile==profile with {AllowedStartingHomes=["other"]},Is.False);
  Assert.That(profile==profile with {AllowedStartingHomes=[]},Is.False);
  Assert.That(profile==profile with {LocalPort=50002},Is.False);
  Assert.That(profile==profile with {ExpectedHomeText="Elsewhere"},Is.False);
 }
}
