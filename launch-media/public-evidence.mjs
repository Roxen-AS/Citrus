// Public receipts use explicit fields; raw tool receipts remain local.
export function assertPublicEvidence(value, label = 'public evidence') {
  const text = JSON.stringify(value);
  if (/\b[a-z]:[\\/]|\\\\[^\\]|\/(?:Users|home|tmp|var\/folders)\/|(?:AppData|generated_images|codex-runtimes|\.codex|\.local)[\\/]|session[_ -]?(?:id|token)/i.test(text)) {
    throw new Error(`Local path or session metadata in ${label}`);
  }
  return value;
}

export function publicGenerationReceipt(raw) {
  return assertPublicEvidence({
    tool: raw.tool,
    date: raw.date,
    role: raw.role,
    source: 'Built-in image generation; original receipt retained locally',
    asset: 'citrus-garden.png',
    prompt: raw.prompt,
    qualification: raw.qualification,
  }, 'generation receipt');
}

export function publicMotionReceipt(raw) {
  const keys = ['source', 'sourceSha256', 'sourceWidth', 'sourceHeight', 'output', 'sha256', 'width', 'height', 'frames', 'fps', 'durationSeconds', 'audio', 'creation', 'encoder'];
  const result = Object.fromEntries(keys.map(key => [key, raw[key]]));
  result.argumentsRelativeTo = 'launch-media/source/';
  result.arguments = raw.arguments.map((argument, index, all) =>
    index === all.length - 1 || all[index - 1] === '-i'
      ? argument.split(/[\\/]/).at(-1)
      : argument);
  return assertPublicEvidence(result, 'motion receipt');
}

export function publicVerificationSummary({ capture, video, image, background, decode, outputFiles }) {
  return assertPublicEvidence({
    version: 1,
    date: '2026-10-21',
    scope: 'Edited native-interface media authoring; not comprehensive release acceptance or a performance benchmark',
    nativeAuthoring: {
      test: 'LaunchMediaCaptureTests.Owned_launch_samples_use_current_native_UI_and_reopened_engine_outputs',
      outcome: 'Passed',
      passed: 1,
      failed: 0,
      skipped: 0,
      renderer: capture.Renderer,
    },
    sourcePreservationVerified: capture.SourcePreservationVerified,
    sourceHashes: {
      'citrus-garden.png': capture.OriginalPhotoSha256,
      'citrus-garden-motion.mp4': capture.OriginalVideoSha256,
    },
    capture: {
      desktopPixelsRead: capture.DesktopPixelsRead,
      captureExclusionChanged: capture.CaptureExclusionChanged,
    },
    outputs: outputFiles,
    reopenedImages: [
      { file: 'sample-image-output.png', format: image.ReopenedFormat, width: image.Width, height: image.Height, sha256: image.Sha256 },
      { file: 'sample-background-output.png', format: background.ReopenedFormat, width: background.Width, height: background.Height, sha256: background.Sha256 },
    ],
    videoTrim: {
      inputDurationSeconds: video.InputDuration,
      startSeconds: video.TrimStart,
      endSeconds: video.TrimEnd,
      reopenedDurationSeconds: video.ReopenedDurationSeconds,
      width: video.Width,
      height: video.Height,
      nativePlaybackFrames: video.PlaybackSamples.length,
    },
    webVideoDecode: decode,
    rawReceipts: 'Preserved in a local authoring archive; excluded from public artifacts',
  }, 'verification summary');
}
