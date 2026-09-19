import { describe, expect, it } from 'vitest';
import {
  baseName,
  outputFileName,
  outputPath,
  validateOutputDir,
  validateOutputFile,
} from './output';

describe('baseName', () => {
  it('strips the extension from the source file name', () => {
    expect(baseName({ path: '/vids/my.clip.mkv' })).toBe('my.clip');
  });
  it('uses saveAs when present', () => {
    expect(baseName({ path: '/vids/in.mkv', saveAs: 'cool video.webm' })).toBe(
      'cool video',
    );
  });
  it('strips a trailing -N suffix', () => {
    expect(baseName({ path: 'clip-1.mkv' })).toBe('clip');
    expect(baseName({ path: 'clip-42' })).toBe('clip');
    expect(baseName({ path: 'clip-1-2.mkv' })).toBe('clip');
  });
  it('keeps hyphens that are not a numeric suffix', () => {
    expect(baseName({ path: 'my-clip.mkv' })).toBe('my-clip');
    expect(baseName({ path: 'clip-x3.mkv' })).toBe('clip-x3');
  });
});

describe('outputFileName', () => {
  it('omits the suffix for index 0', () => {
    expect(outputFileName('clip', 0, 'webm')).toBe('clip.webm');
  });
  it('appends -N for higher indexes', () => {
    expect(outputFileName('clip', 1, 'webm')).toBe('clip-1.webm');
    expect(outputFileName('clip', 12, 'mp4')).toBe('clip-12.mp4');
  });
});

describe('outputPath', () => {
  it('picks the first non-existing candidate', () => {
    const taken = new Set(['/out/clip.webm', '/out/clip-1.webm']);
    expect(outputPath('/out', 'clip', 'webm', (p) => taken.has(p))).toBe(
      '/out/clip-2.webm',
    );
  });
  it('returns the plain name when nothing exists', () => {
    expect(outputPath('/out', 'clip', 'mp4', () => false)).toBe(
      '/out/clip.mp4',
    );
  });
});

describe('validateOutputDir', () => {
  it('rejects a relative path', () => {
    expect(validateOutputDir('out', { exists: true, isDir: true })).toBe(
      'Output directory must be an absolute path',
    );
  });
  it('rejects a missing directory', () => {
    expect(validateOutputDir('/out', { exists: false, isDir: false })).toBe(
      'Output directory does not exist',
    );
  });
  it('rejects a path that is not a directory', () => {
    expect(validateOutputDir('/out', { exists: true, isDir: false })).toBe(
      'Output path is not a directory',
    );
  });
  it('rejects a non-writable directory', () => {
    expect(
      validateOutputDir('/out', { exists: true, isDir: true, writable: false }),
    ).toBe('Output directory is not writable');
  });
  it('accepts a valid writable directory', () => {
    expect(
      validateOutputDir('C:\\out', {
        exists: true,
        isDir: true,
        writable: true,
      }),
    ).toBeNull();
    expect(validateOutputDir('/out', { exists: true, isDir: true })).toBeNull();
  });
});

describe('validateOutputFile', () => {
  it('accepts a missing file', () => {
    expect(validateOutputFile({ exists: false, writable: false })).toBeNull();
  });
  it('accepts a writable existing file', () => {
    expect(validateOutputFile({ exists: true, writable: true })).toBeNull();
  });
  it('rejects a read-only existing file', () => {
    expect(validateOutputFile({ exists: true, writable: false })).toBe(
      'Output file exists and is not writable',
    );
  });
});
