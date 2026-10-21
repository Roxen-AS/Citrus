import test from 'node:test';
import assert from 'node:assert/strict';
import { assertPublicEvidence, publicGenerationReceipt, publicMotionReceipt, publicVerificationSummary } from './public-evidence.mjs';

test('generation receipts retain the exact prompt without machine or session metadata', () => {
  const prompt = 'A citrus photograph. No generated interface.';
  const result = publicGenerationReceipt({ tool: 'imagegen', date: '2026-10-21', role: 'sample photograph', prompt, qualification: 'Native resolution recorded separately', source: 'C:/Users/example/.codex/generated_images/private.png', sessionId: 'private-session' });
  assert.equal(result.prompt, prompt);
  assert.equal(result.asset, 'citrus-garden.png');
  assert.equal('sessionId' in result, false);
  assert.doesNotMatch(JSON.stringify(result), /Users|private/);
});

test('motion receipts preserve encoder controls while making only input/output paths relative', () => {
  const args = ['-i', 'C:\\Users\\example\\photo.png', '-vf', "zoompan=z='1.03'", '-an', '/home/example/movie.mp4'];
  const result = publicMotionReceipt({ source: 'photo.png', output: 'movie.mp4', arguments: args, sessionToken: 'private' });
  assert.deepEqual(result.arguments, ['-i', 'photo.png', '-vf', "zoompan=z='1.03'", '-an', 'movie.mp4']);
  assert.equal(result.argumentsRelativeTo, 'launch-media/source/');
  assert.equal('sessionToken' in result, false);
});

test('public evidence fails closed on absolute paths and session fields', () => {
  for (const value of [{ source: 'C:/Users/example/file.png' }, { source: '\\\\host\\share\\file' }, { source: '/home/example/file' }, { source: '.local/archive/file' }, { sessionId: 'secret' }]) {
    assert.throws(() => assertPublicEvidence(value), /Local path or session/);
  }
  assert.doesNotThrow(() => assertPublicEvidence({ source: 'sample-photo.png', url: 'https://github.com/Skytuhua/Citrus' }));
});

test('verification summaries retain output facts without copying raw run or playback receipts', () => {
  const summary = publicVerificationSummary({
    capture: { Renderer: 'Production native renderer', SourcePreservationVerified: true, OriginalPhotoSha256: 'abc', OriginalVideoSha256: 'def', DesktopPixelsRead: false, CaptureExclusionChanged: false, Host: 'private-machine', SourcePath: 'C:/Users/example/private.png' },
    video: { InputDuration: 8, TrimStart: 1.2, TrimEnd: 6.5, ReopenedDurationSeconds: 5.3, Width: 1280, Height: 720, PlaybackSamples: [{ path: 'C:/Users/example/frame.png' }] },
    image: { ReopenedFormat: 'Png', Width: 1672, Height: 941, Sha256: 'ghi', sessionId: 'private' },
    background: { ReopenedFormat: 'Png', Width: 1992, Height: 1261, Sha256: 'jkl' },
    outputFiles: [{ file: 'sample-image-output.png', sha256: 'ghi' }],
    decode: [{ file: 'citrus-launch.mp4', fullDecode: 'Passed' }],
  });
  assert.equal(summary.sourcePreservationVerified, true);
  assert.equal(summary.videoTrim.reopenedDurationSeconds, 5.3);
  assert.equal(summary.videoTrim.nativePlaybackFrames, 1);
  assert.equal(summary.reopenedImages[0].width, 1672);
  assert.doesNotMatch(JSON.stringify(summary), /private-machine|Users|sessionId|PlaybackSamples/);
});
