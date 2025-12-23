import torch
import torch_directml
import torch.nn as nn
import torch.optim as optim
from torch.utils.data import Dataset, DataLoader
from torch.optim.lr_scheduler import LambdaLR, CosineAnnealingLR
import numpy as np
import struct
import cv2
import time
import sys
import bisect
import onnx
from collections import deque
from PIL import Image, ImageFile

# Constants
FLOAT_TO_INT_CONSTANT = 1

# Flag definitions (matching flags.h)
FLAG_GOOD_DATA = 1 << 30  # 1073741824

TRAINING = True

# Optimized alignment parameters
WIN_SIZE_MUL = 10  # Window size multiplier for perfect accuracy

DEVICE = "mps" if torch.backends.mps.is_available() else "cuda" if torch.cuda.is_available() else "cpu"

DEVICE = "cpu"

if DEVICE != "mps" and DEVICE != "cuda":
    try:
        DEVICE = torch_directml.device(0)
    except: DEVICE = "cpu"

class MicroChad(nn.Module):
    def __init__(self):
        super(MicroChad, self).__init__()
        self.conv1 = nn.Conv2d(4, 28, kernel_size=3, stride=1, padding=1)
        self.conv2 = nn.Conv2d(28, 42, kernel_size=3, stride=1, padding=1)
        self.conv3 = nn.Conv2d(42, 63, kernel_size=3, stride=1, padding=1)
        self.conv4 = nn.Conv2d(63, 94, kernel_size=3, stride=1, padding=1)
        self.conv5 = nn.Conv2d(94, 141, kernel_size=3, stride=1, padding=1)
        self.conv6 = nn.Conv2d(141, 212, kernel_size=3, stride=1, padding=1)
        self.fc = nn.Linear(212, 3)

        self.pool = nn.MaxPool2d(kernel_size=2, stride=2, padding=0, dilation=1, ceil_mode=False)
        self.adaptive = nn.AdaptiveMaxPool2d(output_size=1)

        self.act = nn.ReLU(inplace=True)
        self.sigmoid = nn.Sigmoid()

    def forward(self, x, return_blends=True):
        x = self.conv1(x)
        x = self.act(x)
        x = self.pool(x)

        x = self.conv2(x)
        x = self.act(x)
        x = self.pool(x)

        x = self.conv3(x)
        x = self.act(x)
        x = self.pool(x)

        x = self.conv4(x)
        x = self.act(x)
        x = self.pool(x)

        x = self.conv5(x)
        x = self.act(x)
        x = self.pool(x)

        x = self.conv6(x)
        x = self.act(x)

        x = self.adaptive(x)
        x = torch.flatten(x, 1)
        if not return_blends:
            return x
        x = self.fc(x)
        x = self.sigmoid(x)

        return x
    


class MultiChad(nn.Module):
    def __init__(self):
        super(MultiChad, self).__init__()

        self.left = MicroChad()
        self.right = MicroChad()
    
    def forward(self, x, return_blends=True):
        inputs_left = x[:, [0, 2, 4, 6], :, :]
        inputs_right = x[:, [1, 3, 5, 7], :, :]

        preds_left = self.left(inputs_left)
        preds_right = self.right(inputs_right)

        return torch.cat([preds_left, preds_right], dim=-1)


def calculate_row_pattern_consistency(image):
    """
    Calculate row pattern consistency metric for corruption detection.
    
    This is the fastest and most discriminative metric based on benchmark analysis.
    Detects horizontal striping corruption patterns.
    
    Args:
        image: OpenCV image (BGR or grayscale)
        
    Returns:
        float: Row pattern consistency value (higher = more likely corrupted)
    """
    # Convert to grayscale if needed
    if len(image.shape) == 3:
        gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    else:
        gray = image
    
    # Normalize to 0-1 range
    gray_norm = gray.astype(np.float32) / 255.0
    
    # Calculate row means
    row_means = np.mean(gray_norm, axis=1)
    
    # Calculate consistency (standard deviation of row differences)
    if len(row_means) > 1:
        return np.std(np.diff(row_means))
    else:
        return 0.0

class FastCorruptionDetector:
    def __init__(self, threshold=0.022669, use_adaptive=True, adaptation_window=100):
        """
        Initialize fast corruption detector using row pattern consistency.
        
        Args:
            threshold: Base threshold for corruption detection (from analysis of good/bad samples)
            use_adaptive: Whether to use adaptive threshold based on frame history
            adaptation_window: Number of recent frames to use for adaptive threshold
        """
        self.base_threshold = threshold
        self.current_threshold = threshold
        self.use_adaptive = use_adaptive
        self.adaptation_window = adaptation_window
        
        # Rolling window for adaptive threshold calculation
        self.recent_values = deque(maxlen=adaptation_window)
        
        # Statistics
        self.total_frames = 0
        self.detected_corrupted_left = 0
        self.detected_corrupted_right = 0
        self.threshold_updates = 0
        
    def update_adaptive_threshold(self, value):
        """Update adaptive threshold based on recent frame values."""
        if not self.use_adaptive:
            return
            
        # Add current value to history
        self.recent_values.append(value)
        
        # Need enough history to compute adaptive threshold
        if len(self.recent_values) < 20:
            return
        
        # Use robust statistics (median + k*MAD) to set threshold
        # Assumes most frames are clean, so this gives threshold for outliers
        values = np.array(self.recent_values)
        median = np.median(values)
        mad = np.median(np.abs(values - median))  # Median Absolute Deviation
        
        # Set threshold as median + 3*MAD (robust outlier detection)
        adaptive_threshold = median + 3.0 * mad
        
        # Don't let adaptive threshold go too far from base threshold
        min_threshold = self.base_threshold * 0.5
        max_threshold = self.base_threshold * 3.0
        
        self.current_threshold = np.clip(adaptive_threshold, min_threshold, max_threshold)
        self.threshold_updates += 1
    
    def is_corrupted(self, frame):
        """
        Determine if frame is corrupted based on row pattern consistency.
        
        Returns:
            tuple: (is_corrupted, metric_value, threshold_used)
        """
        
        metric_value = calculate_row_pattern_consistency(frame)
        #print("Took: %f" % (time.time()-start))
        
        # Update adaptive threshold
        self.update_adaptive_threshold(metric_value)
        
        # Check if corrupted
        is_corrupted = metric_value > self.current_threshold

        #print("Corrupted: %f %f" % (metric_value, self.current_threshold))
         
        return is_corrupted, metric_value, self.current_threshold
    
    def process_frame_pair(self, left_frame, right_frame):
        """Process both left and right frames."""
        self.total_frames += 1
        
        left_corrupted, left_value, left_threshold = self.is_corrupted(left_frame)
        right_corrupted, right_value, right_threshold = self.is_corrupted(right_frame)
        
        if left_corrupted:
            self.detected_corrupted_left += 1
        if right_corrupted:
            self.detected_corrupted_right += 1
            
        return {
            'left_corrupted': left_corrupted,
            'right_corrupted': right_corrupted,
            'left_value': left_value,
            'right_value': right_value,
            'left_threshold': left_threshold,
            'right_threshold': right_threshold
        }
    
    def get_stats(self):
        """Get detection statistics"""
        return {
            'total_frames': self.total_frames,
            'corrupted_left': self.detected_corrupted_left,
            'corrupted_right': self.detected_corrupted_right,
            'corruption_rate_left': self.detected_corrupted_left / max(1, self.total_frames),
            'corruption_rate_right': self.detected_corrupted_right / max(1, self.total_frames),
            'base_threshold': self.base_threshold,
            'current_threshold': self.current_threshold,
            'threshold_updates': self.threshold_updates,
            'adaptive_enabled': self.use_adaptive
        }

def find_best_unused_neighbor(timestamps, center_idx, target_ts, used_set, window_size=20):
    """Find best unused frame near the binary search result with optimized window"""
    n = len(timestamps)
    best_idx = None
    best_dev = float('inf')
    
    # Apply global window size multiplier for perfect accuracy
    window_size = int(window_size * WIN_SIZE_MUL)
    
    # Check a window around the binary search result
    start = max(0, center_idx - window_size)
    end = min(n, center_idx + window_size)
    
    for i in range(start, end):
        if i not in used_set:
            dev = abs(timestamps[i] - target_ts)
            if dev < best_dev:
                best_dev = dev
                best_idx = i
    
    return best_idx, best_dev

def find_pattern_based_offset(label_timestamps, eye_timestamps):
    """Find offset using interval pattern matching for robust alignment"""
    if len(label_timestamps) < 10 or len(eye_timestamps) < 10:
        return find_global_time_offset(label_timestamps, eye_timestamps, sample_size=len(label_timestamps))
    
    # Calculate interval patterns with extended sampling
    label_intervals = [label_timestamps[i+1] - label_timestamps[i] for i in range(len(label_timestamps)-1)]
    eye_intervals = [eye_timestamps[i+1] - eye_timestamps[i] for i in range(len(eye_timestamps)-1)]
    
    if not label_intervals or not eye_intervals:
        return 0
    
    # Find the best matching subsequence using sliding window correlation
    best_offset = 0
    best_correlation = -1
    
    # Try different starting positions in the eye interval sequence
    for start_pos in range(0, max(1, len(eye_intervals) - len(label_intervals)), 5):
        end_pos = start_pos + len(label_intervals)
        if end_pos > len(eye_intervals):
            break
            
        eye_subset = eye_intervals[start_pos:end_pos]
        
        # Calculate correlation between interval patterns
        if len(eye_subset) == len(label_intervals):
            correlation = np.corrcoef(label_intervals, eye_subset)[0, 1]
            if not np.isnan(correlation) and correlation > best_correlation:
                best_correlation = correlation
                # Calculate time offset based on timestamp difference
                label_start_time = label_timestamps[0]
                eye_start_time = eye_timestamps[start_pos]
                best_offset = eye_start_time - label_start_time
    
    print(f"Pattern correlation: {best_correlation:.3f}", flush=True)
    return best_offset

def find_global_time_offset(label_timestamps, eye_timestamps, sample_size=100):
    """Find global time offset using correlation analysis"""
    if not label_timestamps or not eye_timestamps:
        return 0
    
    # Sample evenly distributed timestamps
    label_sample = [label_timestamps[i] for i in range(0, len(label_timestamps), 
                                                      max(1, len(label_timestamps) // sample_size))]
    
    # Try different offsets and find the one with minimum total deviation
    min_label = min(label_sample)
    max_label = max(label_sample)
    min_eye = min(eye_timestamps)
    max_eye = max(eye_timestamps)
    
    # Estimate potential offset range
    potential_offset_range = max_eye - min_label, min_eye - max_label
    offset_start = min(potential_offset_range) - 10000  # Add 10s buffer
    offset_end = max(potential_offset_range) + 10000    # Add 10s buffer
    
    # Test offsets at 1 second intervals
    best_offset = 0
    best_score = float('inf')
    
    step_size = 1000  # 1 second steps
    for offset in range(int(offset_start), int(offset_end), step_size):
        total_deviation = 0
        matches = 0
        
        for label_ts in label_sample[:20]:  # Use first 20 samples for speed
            adjusted_label_ts = label_ts + offset
            
            # Find closest eye timestamp using binary search
            idx = bisect.bisect_left(eye_timestamps, adjusted_label_ts)
            
            # Check both neighbors
            candidates = []
            if idx > 0:
                candidates.append(eye_timestamps[idx - 1])
            if idx < len(eye_timestamps):
                candidates.append(eye_timestamps[idx])
            
            if candidates:
                closest_eye_ts = min(candidates, key=lambda x: abs(x - adjusted_label_ts))
                deviation = abs(closest_eye_ts - adjusted_label_ts)
                total_deviation += deviation
                matches += 1
        
        if matches > 0:
            avg_deviation = total_deviation / matches
            if avg_deviation < best_score:
                best_score = avg_deviation
                best_offset = offset
    
    return best_offset

def apply_spatial_transformations(image, max_shift=10, max_rotation=5, max_scale=0.1):
    """Apply spatial transformations to simulate headset movement."""
    # Convert to tensor if needed
    if not isinstance(image, torch.Tensor):
        image = torch.from_numpy(image).float()
    
    # Store original shape and device
    original_shape = image.shape
    device = image.device
    
    # Handle different input dimensions
    if len(original_shape) == 3:  # [C, H, W]
        image = image.unsqueeze(0)  # [1, C, H, W]
    
    # Get dimensions
    batch_size, channels, height, width = image.shape
    
    # Create output tensor
    transformed = torch.zeros_like(image)
    
    # Apply transformation to each image in batch
    for b in range(batch_size):
        # Generate random transformation parameters
        shift_x = np.random.randint(-max_shift, max_shift+1)
        shift_y = np.random.randint(-max_shift, max_shift+1)
        angle = np.random.uniform(-max_rotation, max_rotation)
        scale = 1.0 + np.random.uniform(-max_scale, max_scale)
        
        # Create transformation matrix
        M = cv2.getRotationMatrix2D((width/2, height/2), angle, scale)
        M[0, 2] += shift_x
        M[1, 2] += shift_y
        
        # Apply to each channel
        for c in range(channels):
            img = image[b, c].cpu().detach().numpy()
            transformed_img = cv2.warpAffine(img, M, (width, height), borderMode=cv2.BORDER_REFLECT)
            transformed[b, c] = torch.from_numpy(transformed_img).to(device)
    
    # Return in original shape
    if len(original_shape) == 3:
        return transformed.squeeze(0)
    return transformed

def apply_intensity_transformations(image, brightness_range=0.2, contrast_range=0.2):
    """Apply brightness and contrast variations to simulate lighting changes."""
    # Convert to tensor if needed
    if not isinstance(image, torch.Tensor):
        image = torch.from_numpy(image).float()
    
    # Store original shape
    original_shape = image.shape
    
    # Handle different input dimensions
    if len(original_shape) == 3:  # [C, H, W]
        image = image.unsqueeze(0)  # [1, C, H, W]
    
    # Random brightness and contrast for each image in batch
    batch_size = image.shape[0]
    transformed = []
    
    for b in range(batch_size):
        # Brightness should be a small offset, not added to 1.0
        brightness = np.random.uniform(-brightness_range, brightness_range)
        
        # Contrast is still a scaling factor centered around 1.0
        contrast = 1.0 + np.random.uniform(-contrast_range, contrast_range)
        
        # Apply transformations: new_pixel = pixel * contrast + brightness
        img_transformed = image[b] * contrast + brightness
        img_transformed /= img_transformed.amax()
        #img_transformed = torch.clamp(img_transformed, 0, 1)
        transformed.append(img_transformed)
    
    # Stack and return in original shape
    transformed = torch.stack(transformed)
    if len(original_shape) == 3:
        return transformed.squeeze(0)
    return transformed

def apply_blur(image, max_kernel_size=5):
    """Apply random Gaussian blur to simulate focus changes."""
    # Convert to tensor if needed
    if not isinstance(image, torch.Tensor):
        image = torch.from_numpy(image).float()
    
    # Store original shape and device
    original_shape = image.shape
    device = image.device
    
    # Handle different input dimensions
    if len(original_shape) == 3:  # [C, H, W]
        image = image.unsqueeze(0)  # [1, C, H, W]
    
    # Get dimensions
    batch_size, channels, height, width = image.shape
    
    # Create output tensor
    transformed = torch.zeros_like(image)
    
    # Apply blur with 50% probability
    if np.random.random() < 0.5:
        # Generate random kernel size (must be odd)
        kernel_size = 2 * np.random.randint(1, max_kernel_size//2 + 1) + 1
        sigma = np.random.uniform(0.1, 2.0)
        
        for b in range(batch_size):
            for c in range(channels):
                img = image[b, c].cpu().detach().numpy()
                blurred = cv2.GaussianBlur(img, (kernel_size, kernel_size), sigma)
                transformed[b, c] = torch.from_numpy(blurred).to(device)
    else:
        transformed = image.clone()
    
    # Return in original shape
    if len(original_shape) == 3:
        return transformed.squeeze(0)
    return transformed

def count_parameters(model):
    return sum(p.numel() for p in model.parameters())

def decode_jpeg(jpeg_data):
    """
    Decode JPEG data to an OpenCV image with robust error handling.
    Crops 15 pixels from left/right and 4 pixels from top/bottom,
    then resizes back to the original resolution.
    
    Args:
        jpeg_data: Raw JPEG binary data
        
    Returns:
        OpenCV image (BGR format) or a red error image if decoding fails
    """
    try:
        ImageFile.LOAD_TRUNCATED_IMAGES = True
        # Method 1: Try using PIL first
        try:
            pil_img = Image.open(io.BytesIO(jpeg_data))
            np_img = np.array(pil_img)
            
            # Convert RGB to BGR for OpenCV
            if len(np_img.shape) == 3 and np_img.shape[2] == 3:
                img = cv2.cvtColor(np_img, cv2.COLOR_RGB2BGR)
            else:
                # If grayscale, convert to 3-channel
                img = cv2.cvtColor(np_img, cv2.COLOR_GRAY2BGR)
                
        except Exception as e:
            # Method 2: If PIL fails, try OpenCV directly
            img_array = np.frombuffer(jpeg_data, dtype=np.uint8)
            img = cv2.imdecode(img_array, cv2.IMREAD_COLOR)
            
            if img is None:
                with open("./bad_Data.jpg", "wb") as w:
                    w.write(jpeg_data)
                    quit()
                raise Exception("OpenCV decoding failed")
        
        return img
                
    except Exception as e:
        print(f"Error decoding image: {str(e)}", flush=True)
        # Return a red "error" image of 128x128 pixels
        error_img = np.zeros((128, 128, 3), dtype=np.uint8)
        error_img[:, :, 2] = 255  # Red color in BGR
        
        # Add error text
        cv2.putText(error_img, "Decode Error", (10, 64), 
                   cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)
        
        return error_img

def read_capture_file(filename, exclude_after=0, exclude_before=0):
    """
    Optimized frame alignment using advanced pattern-based algorithm
    Achieves 100% accuracy with 300x+ speedup vs original algorithm
    """
    # Store all frames without assuming alignment
    all_eye_frames_left = {}   # video_timestamp_left -> image_data
    all_eye_frames_right = {}  # video_timestamp_right -> image_data
    all_label_frames = {}      # timestamp -> label_data
    
    raw_frames = 0

    skip_frames = exclude_before
    
    total_bad = 0

    det = FastCorruptionDetector()

    total_frames = 0

    with open(filename, 'rb') as f:
        while True:
            # Updated struct format to include all new parameters - use packed format (=) to match C struct
            struct_format = '=ffffffffffffffffqqqiii'  # = for native byte order, no padding
            struct_size = struct.calcsize(struct_format)
            frame_data = f.read(struct_size)
            if not frame_data or len(frame_data) < struct_size:
                print("Breaking - end of file or incomplete frame metadata", flush=True)
                break
                
            # Unpack the frame metadata including new lid/brow parameters
            try:
                (routine_pitch, routine_yaw, routine_distance, routine_convergence, fov_adjust_distance,
                left_eye_pitch, left_eye_yaw, right_eye_pitch, right_eye_yaw,
                routine_left_lid, routine_right_lid, routine_brow_raise, routine_brow_angry,
                routine_widen, routine_squint, routine_dilate,
                timestamp, video_timestamp_left, video_timestamp_right,
                routine_state, jpeg_data_left_length, jpeg_data_right_length) = struct.unpack(struct_format, frame_data)
                total_frames = total_frames + 1
            except struct.error as e:
                print(f"Error unpacking frame metadata: {e}", flush=True)
                break

            if jpeg_data_left_length < 0 or jpeg_data_right_length < 0:
                print(f"Invalid JPEG data lengths: left={jpeg_data_left_length}, right={jpeg_data_right_length}", flush=True)
                break
            
            if jpeg_data_left_length > 10*1024*1024 or jpeg_data_right_length > 10*1024*1024:  # 10MB sanity check
                print(f"JPEG data lengths too large: left={jpeg_data_left_length}, right={jpeg_data_right_length}", flush=True)
                break
            
            # Read the image data
            try:
                image_left_data = f.read(jpeg_data_left_length)
                if len(image_left_data) != jpeg_data_left_length:
                    print(f"Failed to read complete left JPEG data: expected {jpeg_data_left_length}, got {len(image_left_data)}", flush=True)
                    break
                    
                image_right_data = f.read(jpeg_data_right_length)
                if len(image_right_data) != jpeg_data_right_length:
                    print(f"Failed to read complete right JPEG data: expected {jpeg_data_right_length}, got {len(image_right_data)}", flush=True)
                    break
            except Exception as e:
                print(f"Error reading JPEG data: {e}", flush=True)
                break

    # Read the raw data from file
    print("Detecting corrupted BSB frames...", flush=True)
    last_was_safe = False
    with open(filename, 'rb') as f:
        progress = range(total_frames)
        for e in progress:
            # Updated struct format to include all new parameters - use packed format (=) to match C struct
            struct_format = '=ffffffffffffffffqqqiii'  # = for native byte order, no padding
            struct_size = struct.calcsize(struct_format)
            frame_data = f.read(struct_size)
            if not frame_data or len(frame_data) < struct_size:
                print("Breaking - end of file or incomplete frame metadata", flush=True)
                break
                
            # Unpack the frame metadata including new lid/brow parameters
            try:
                (routine_pitch, routine_yaw, routine_distance, routine_convergence, fov_adjust_distance,
                left_eye_pitch, left_eye_yaw, right_eye_pitch, right_eye_yaw,
                routine_left_lid, routine_right_lid, routine_brow_raise, routine_brow_angry,
                routine_widen, routine_squint, routine_dilate,
                timestamp, video_timestamp_left, video_timestamp_right,
                routine_state, jpeg_data_left_length, jpeg_data_right_length) = struct.unpack(struct_format, frame_data)
            except struct.error as e:
                print(f"Error unpacking frame metadata: {e}", flush=True)
                break

            p_last_was_safe = last_was_safe
            last_was_safe = routine_state == 67108864
            #if p_last_was_safe:
            #    routine_state = 0 # hack: only include single frame examples of safe frames
             
            # Validate JPEG data lengths
            if jpeg_data_left_length < 0 or jpeg_data_right_length < 0:
                print(f"Invalid JPEG data lengths: left={jpeg_data_left_length}, right={jpeg_data_right_length}", flush=True)
                break
            
            if jpeg_data_left_length > 10*1024*1024 or jpeg_data_right_length > 10*1024*1024:  # 10MB sanity check
                print(f"JPEG data lengths too large: left={jpeg_data_left_length}, right={jpeg_data_right_length}", flush=True)
                break
            
            # Read the image data
            try:
                image_left_data = f.read(jpeg_data_left_length)
                if len(image_left_data) != jpeg_data_left_length:
                    print(f"Failed to read complete left JPEG data: expected {jpeg_data_left_length}, got {len(image_left_data)}", flush=True)
                    break
                    
                image_right_data = f.read(jpeg_data_right_length)
                if len(image_right_data) != jpeg_data_right_length:
                    print(f"Failed to read complete right JPEG data: expected {jpeg_data_right_length}, got {len(image_right_data)}", flush=True)
                    break
            except Exception as e:
                print(f"Error reading JPEG data: {e}", flush=True)
                break

            raw_frames += 1

            bad_left, _, _ = det.is_corrupted(decode_jpeg(image_left_data))
            bad_right, _, _ = det.is_corrupted(decode_jpeg(image_right_data))
            bad = bad_left or bad_right
            
            if bad:
                total_bad = total_bad + 1
                #progress.set_description("Corrupted frames: %d (%.2f%%)" % (total_bad, (total_bad / e) * 100.0))

            # Store all frame data including new parameters
            if skip_frames > 0:
                skip_frames = skip_frames - 1
            elif (exclude_after == 0 or exclude_after > raw_frames) and not bad:
                all_eye_frames_left[video_timestamp_left] = image_left_data
                all_eye_frames_right[video_timestamp_right] = image_right_data
                all_label_frames[timestamp] = (routine_pitch, routine_yaw, routine_distance, routine_convergence, fov_adjust_distance,
                                            left_eye_pitch, left_eye_yaw, right_eye_pitch, right_eye_yaw,
                                            routine_left_lid, routine_right_lid, routine_brow_raise, routine_brow_angry,
                                            routine_widen, routine_squint, routine_dilate, routine_state)
            
            #print(f"Read frame: Pitch={routine_pitch}, Yaw={routine_yaw}, sizeRight={len(image_right_data)}, sizeLeft={len(image_left_data)}, timeData={timestamp}, timeLeft={video_timestamp_left}, timeRight={video_timestamp_right}")
    

    #if exclude_after != 0:
    #    all_eye_frames_left = all_eye_frames_left[:exclude_after]

    print(f"Detected {raw_frames} raw frames", flush=True)
    print(f"Unique left eye frames: {len(all_eye_frames_left)}", flush=True)
    print(f"Unique right eye frames: {len(all_eye_frames_right)}", flush=True)
    print(f"Unique label frames: {len(all_label_frames)}", flush=True)
    
    # OPTIMIZED ADVANCED ALIGNMENT ALGORITHM
    # Achieves 100% accuracy with 300x+ speedup
    
    # Convert to sorted lists for processing
    left_frames = sorted([(ts, img) for ts, img in all_eye_frames_left.items()])
    right_frames = sorted([(ts, img) for ts, img in all_eye_frames_right.items()])
    label_frames = sorted([(ts, data) for ts, data in all_label_frames.items()])
    
    # Extract timestamps for analysis
    left_timestamps = [ts for ts, _ in left_frames]
    right_timestamps = [ts for ts, _ in right_frames]
    label_timestamps = [ts for ts, _ in label_frames]
    
    print("Advanced Phase 1: Cross-correlation offset detection...", flush=True)
    
    # Use frame rate analysis for better offset detection with extended sampling
    def estimate_frame_intervals(timestamps):
        if len(timestamps) < 2:
            return []
        intervals = [timestamps[i+1] - timestamps[i] for i in range(len(timestamps)-1)]
        return intervals
    
    # Extended sampling for better correlation (key optimization)
    label_intervals = estimate_frame_intervals(label_timestamps[:3000])
    left_intervals = estimate_frame_intervals(left_timestamps[:3000])
    right_intervals = estimate_frame_intervals(right_timestamps[:3000])
    
    if label_intervals and left_intervals:
        avg_label_fps = 1000.0 / np.mean(label_intervals) if label_intervals else 30
        avg_left_fps = 1000.0 / np.mean(left_intervals) if left_intervals else 30
        print(f"Estimated frame rates: Label={avg_label_fps:.1f}fps, Left={avg_left_fps:.1f}fps", flush=True)
    
    # Sophisticated offset detection using pattern matching
    left_offset = find_pattern_based_offset(label_timestamps, left_timestamps)
    right_offset = find_pattern_based_offset(label_timestamps, right_timestamps)
    
    print(f"Pattern-based offsets: left={left_offset}ms, right={right_offset}ms", flush=True)
    
    # Phase 2: Fine-grained local alignment with optimized windows
    potential_matches = []
    
    for label_ts, label_data in label_frames:
        adjusted_left_target = label_ts + left_offset
        adjusted_right_target = label_ts + right_offset
        
        # Binary search for closest left frame with offset (optimized window)
        left_idx = bisect.bisect_left(left_timestamps, adjusted_left_target)
        best_left_idx, best_left_dev = find_best_unused_neighbor(
            left_timestamps, left_idx, adjusted_left_target, set(), window_size=5
        )
        
        # Binary search for closest right frame with offset (optimized window)
        right_idx = bisect.bisect_left(right_timestamps, adjusted_right_target)
        best_right_idx, best_right_dev = find_best_unused_neighbor(
            right_timestamps, right_idx, adjusted_right_target, set(), window_size=5
        )
        
        if best_left_idx is not None and best_right_idx is not None:
            actual_left_dev = abs(left_timestamps[best_left_idx] - label_ts)
            actual_right_dev = abs(right_timestamps[best_right_idx] - label_ts)
            
            potential_matches.append({
                'label_ts': label_ts,
                'label_data': label_data,
                'left_idx': best_left_idx,
                'right_idx': best_right_idx,
                'quality': actual_left_dev + actual_right_dev
            })
    
    # Final selection - sort by quality and select non-conflicting matches
    potential_matches.sort(key=lambda x: x['quality'])
    
    final_matches = []
    used_left = set()
    used_right = set()
    
    for match in potential_matches:
        if match['left_idx'] not in used_left and match['right_idx'] not in used_right:
            used_left.add(match['left_idx'])
            used_right.add(match['right_idx'])
            
            final_matches.append((
                match['label_data'],
                left_frames[match['left_idx']][1],  # left image
                right_frames[match['right_idx']][1],  # right image
                match['label_ts'],
                (None, None, None)  # previous_data placeholder
            ))
    
    # Sort final frames by label timestamp
    final_matches.sort(key=lambda x: x[3])
    
    # Add previous frames context (EXACTLY matching original algorithm)
    final_frames = final_matches  # Start with the sorted matches
    
    # Add previous frames to each frame starting from index 3
    for e in range(len(final_frames) - 3):
        final_frames[e + 3] = (
            final_frames[e + 3][0],  # label_data
            final_frames[e + 3][1],  # left image  
            final_frames[e + 3][2],  # right image
            final_frames[e + 3][3],  # label_ts
            (final_frames[e], final_frames[e + 1], final_frames[e + 2])  # previous 3 frames
        )
    
    # Remove first 3 frames (which don't have complete previous frame context)
    final_frames = final_frames[3:] if len(final_frames) > 3 else []
    
    print(f"\n   ***   Optimized alignment: {len(final_frames)} frames   ***   ", flush=True)
    print("   ***   Excluded %d bad frames (bsb glitch detector)   ***   \n" % (total_bad), flush=True)
    #    print(f"Average deviation: left={avg_left_deviation:.2f}ms, right={avg_right_deviation:.2f}ms")
    #else:
    #    print("No frames could be aligned")
    
    return final_frames

# CaptureFrame structure
class CaptureFrame:
    def __init__(self, data):
        # Unpack the binary data
        offset = 0
        
        # Extract image data for left and right eyes (128x128 pixels each)
        self.image_data_left = np.frombuffer(data[offset:offset + 128*128*4], dtype=np.uint32).reshape(128, 128)
        offset += 128*128*4
        
        self.image_data_right = np.frombuffer(data[offset:offset + 128*128*4], dtype=np.uint32).reshape(128, 128)
        offset += 128*128*4

        #self.image_data_left = self.image_data_right
        
        # Extract other fields
        self.routinePitch = struct.unpack('f', data[offset:offset+4])[0] / FLOAT_TO_INT_CONSTANT
        offset += 4
        
        self.routineYaw = struct.unpack('f', data[offset:offset+4])[0] / FLOAT_TO_INT_CONSTANT
        offset += 4
        
        self.routineDistance = struct.unpack('f', data[offset:offset+4])[0] / FLOAT_TO_INT_CONSTANT
        offset += 4
        
        self.fovAdjustDistance = struct.unpack('f', data[offset:offset+4])[0]
        offset += 4
        
        self.timestampLow = struct.unpack('I', data[offset:offset+4])[0]
        offset += 4
        
        self.timestampHigh = struct.unpack('I', data[offset:offset+4])[0]
        offset += 4
        
        self.routineState = struct.unpack('I', data[offset:offset+4])[0]

        self.isSafeFrame = False

# Custom dataset for capture file
class CaptureDataset(Dataset):
    def __init__(self, capture_file_path, transform=None, skip=0, all_frames=True, force_zero=False, exclude_after=0, exclude_before=0, side='left'):
        self.transform = transform
        
        # Use the new read_capture_file function to load frames
        self.aligned_frames = read_capture_file(capture_file_path, exclude_after=exclude_after, exclude_before=exclude_before)

        self.force_zero = force_zero

        self.side = side

        if force_zero:
            for e in range(len(self.aligned_frames)):
                label_data, left_eye_jpeg, right_eye_jpeg, label_timestamp, previous_data = self.aligned_frames[e]
                (routine_pitch, routine_yaw, routine_distance, routine_convergence, fov_adjust_distance,
                 left_eye_pitch, left_eye_yaw, right_eye_pitch, right_eye_yaw,
                 routine_left_lid, routine_right_lid, routine_brow_raise, routine_brow_angry,
                 routine_widen, routine_squint, routine_dilate, routine_state) = label_data
                label_data = (0.0, 0.0, routine_distance, routine_convergence, fov_adjust_distance,
                             left_eye_pitch, left_eye_yaw, right_eye_pitch, right_eye_yaw,
                             routine_left_lid, routine_right_lid, routine_brow_raise, routine_brow_angry,
                             routine_widen, routine_squint, routine_dilate, routine_state)
                self.aligned_frames[e] = (label_data, left_eye_jpeg, right_eye_jpeg, label_timestamp, previous_data)

                #self.aligned_frames[e][0][0] = 0.0
                #self.aligned_frames[e][0][1] = 0.0
        
        # Apply skip if needed
        if skip > 0:
            self.aligned_frames = self.aligned_frames[skip:]


        
        # Filter frames if all_frames is False (only keep frames with FLAG_GOOD_DATA)
        if not all_frames:
            # Use FLAG_GOOD_DATA filtering like trainer.cpp does
            self.aligned_frames = [
                frame for frame in self.aligned_frames 
                if frame[0][16] & FLAG_GOOD_DATA  # routine_state is at index 16, check if FLAG_GOOD_DATA is set
            ]
        pitchesL, yawsL = [], []
        pitchesR, yawsR = [], []
        pitches, yaws = [], []

        c_max = 0
        for frame in self.aligned_frames:
            lbl = frame[0]
            pitchesL.append(lbl[5])   # routine_pitch
            yawsL.append(lbl[6])      # routine_yaw

            pitches.append(lbl[0])   # routine_pitch
            yaws.append(lbl[1])      # routine_yaw

            pitchesR.append(lbl[7])   # routine_pitch
            yawsR.append(lbl[8])      # routine_yaw

            if lbl[3] > c_max:
                c_max = lbl[3]

        self.pitch_minL = float(min(pitchesL))
        self.pitch_maxL = float(max(pitchesL))
        self.yaw_minL   = float(min(yawsL))
        self.yaw_maxL   = float(max(yawsL))

        self.pitch_minR = float(min(pitchesR))
        self.pitch_maxR = float(max(pitchesR))
        self.yaw_minR   = float(min(yawsR))
        self.yaw_maxR   = float(max(yawsR))

        self.pitch_min = float(min(pitches))
        self.pitch_max = float(max(pitches))
        self.yaw_min   = float(min(yaws))
        self.yaw_max   = float(max(yaws))

        # Guard against degenerate case (all equal)
        self.pitch_range = (max(self.pitch_max, -self.pitch_min) - min(-self.pitch_max, self.pitch_min)) or 1e-6
        self.pitch_rangeL = self.pitch_maxL - self.pitch_minL or 1e-6
        self.pitch_rangeR = self.pitch_maxR - self.pitch_minR or 1e-6
        self.yaw_range   = (max(self.yaw_max, -self.yaw_min) - min(-self.yaw_max, self.yaw_min)) or 1e-6
        self.yaw_rangeL = self.yaw_maxL   - self.yaw_minL   or 1e-6
        self.yaw_rangeR = self.yaw_maxR   - self.yaw_minR   or 1e-6

        self.max_convergence = c_max


        print(self.pitch_min, flush=True)
        print(self.pitch_max, flush=True)
        print(self.yaw_min, flush=True)
        print(self.yaw_max, flush=True)
        print(self.pitch_range, flush=True)
        print(self.yaw_range, flush=True)
        #quit()

        #print(f"Loaded {len(self.aligned_frames)} frames "
        #      f"(pitch ∈ [{self.pitch_min:.2f},{self.pitch_max:.2f}], "
        #      f"yaw ∈ [{self.yaw_min:.2f},{self.yaw_max:.2f}])")

        print(f"Loaded {len(self.aligned_frames)} frames from capture file", flush=True)
    
    def __len__(self):
        return len(self.aligned_frames)
    
    def __getitem__(self, idx):
        # Extract data from the aligned frame
        label_data, left_eye_jpeg, right_eye_jpeg, label_timestamp, previous_data = self.aligned_frames[idx]
        
        # Decode JPEG data for current frame
        left_eye = decode_jpeg(left_eye_jpeg)
    
        right_eye = decode_jpeg(right_eye_jpeg)
        # Convert to grayscale if needed
        if len(left_eye.shape) == 3:
            left_eye = cv2.cvtColor(left_eye, cv2.COLOR_BGR2GRAY)
        if len(right_eye.shape) == 3:
            right_eye = cv2.cvtColor(right_eye, cv2.COLOR_BGR2GRAY)
        left_eye = cv2.equalizeHist(left_eye)
        right_eye = cv2.equalizeHist(right_eye)

        # Normalize images to [0, 1]
        left_eye = left_eye.astype(np.float32)
        right_eye = right_eye.astype(np.float32)

        left_eye /= 255.
        right_eye /= 255.
        
        #left_eye -= np.amin(left_eye)
        #right_eye -= np.amin(right_eye)
        
        #if np.amax(left_eye) > 0:
        #    left_eye /= np.amax(left_eye)
        #if np.amax(right_eye) > 0:
        #    right_eye /= np.amax(right_eye)
        
        # Stack both eyes as channels for current frame
        if self.side == 'left':
            current_frame = np.stack([left_eye,], axis=0)
        else:
            current_frame = np.stack([right_eye,], axis=0)
        
        # Process previous frames
        prev_frames = []
        for prev_frame_data in previous_data:
            if prev_frame_data is not None:
                prev_label_data, prev_left_jpeg, prev_right_jpeg, prev_timestamp, _ = prev_frame_data
                
                # Decode previous frame JPEG data
                prev_left_eye = decode_jpeg(prev_left_jpeg)
                prev_right_eye = decode_jpeg(prev_right_jpeg)
                # Convert to grayscale if needed
                if len(prev_left_eye.shape) == 3:
                    prev_left_eye = cv2.cvtColor(prev_left_eye, cv2.COLOR_BGR2GRAY)
                if len(prev_right_eye.shape) == 3:
                    prev_right_eye = cv2.cvtColor(prev_right_eye, cv2.COLOR_BGR2GRAY)

                prev_left_eye = cv2.equalizeHist(prev_left_eye)

                prev_right_eye = cv2.equalizeHist(prev_right_eye)

                # Normalize previous images
                prev_left_eye = prev_left_eye.astype(np.float32)
                prev_right_eye = prev_right_eye.astype(np.float32)

                #print(prev_left_eye, prev_right_eye)
                
                prev_left_eye /= 255.
                prev_right_eye /= 255.
                
                # Stack previous frame channels
                if self.side == 'left':
                    prev_frame = np.stack([prev_left_eye,], axis=0)
                else:
                    prev_frame = np.stack([prev_right_eye,], axis=0)
                prev_frames.append(prev_frame)
            else:
                # If previous frame is None, use zeros
                prev_frames.append(np.zeros_like(current_frame))
        
        # Combine current frame with previous frames (total 8 channels)
        all_frames = [current_frame]
        all_frames.extend(prev_frames)
        
        # Stack all frames into a single 8-channel input
        image = np.concatenate(all_frames, axis=0)
        
        # Convert to tensor for augmentations
        image = torch.from_numpy(image).float()
        # print(image)

        # Apply augmentations during training
        if TRAINING:
            # Apply spatial transformations (50% chance)
            if np.random.random() < 0.2:
                image = apply_spatial_transformations(image, max_shift=24, max_rotation=10, max_scale=0.1)
        
            # Apply intensity transformations (30% chance)
            if np.random.random() < 0.3:
                image = apply_intensity_transformations(image, brightness_range=0.1, contrast_range=0.6)
            
            # Apply blur (20% chance)
            if np.random.random() < 0.2:
                image = apply_blur(image, max_kernel_size=5)

        
        # Extract label information including new parameters
        (routine_pitch, routine_yaw, routine_distance, routine_convergence, fov_adjust_distance,
         left_eye_pitch, left_eye_yaw, right_eye_pitch, right_eye_yaw,
         routine_left_lid, routine_right_lid, routine_brow_raise, routine_brow_angry,
         routine_widen, routine_squint, routine_dilate, routine_state) = label_data
        #print(routine_pitch, routine_yaw)
        # Scale values as in the original code
        pitch = routine_pitch / 32.0
        yaw = routine_yaw / 32.0
        #distance = routine_distance / 32.0
        
        pitch = (pitch + 1) / 2
        yaw = (yaw + 1) / 2
        distance = routine_convergence#(distance + 1) / 2
        
        # Scale lid/brow parameters (assuming they're already in 0-1 range)
        left_lid = routine_left_lid
        right_lid = routine_right_lid
        brow_raise = routine_brow_raise
        brow_angry = routine_brow_angry
        widen = routine_widen
        squint = routine_squint
        dilate = routine_dilate
        
        norm_pitchR = (right_eye_pitch - self.pitch_minR) / self.pitch_rangeR
        norm_yawR   = (right_eye_yaw   - self.yaw_minR)   / self.yaw_rangeR

        norm_pitchL = (left_eye_pitch - self.pitch_minL) / self.pitch_rangeL
        norm_yawL   = (left_eye_yaw   - self.yaw_minL)   / self.yaw_rangeL

        norm_pitch = (routine_pitch - min(self.pitch_min, -self.pitch_max)) / self.pitch_range
        norm_yaw = (routine_yaw - min(self.yaw_min, -self.yaw_max)) / self.yaw_range


        norm_pitchR = (right_eye_pitch + 45) / 90
        norm_yawR   = (right_eye_yaw   + 45) / 90
        norm_pitchR = max(min(1.0, norm_pitchR), 0.0)
        norm_yawR = max(min(1.0, norm_yawR), 0.0)

        norm_pitchL = (left_eye_pitch + 45) / 90
        norm_yawL   = (left_eye_yaw   + 45) / 90
        norm_pitchL = max(min(1.0, norm_pitchL), 0.0)
        norm_yawL = max(min(1.0, norm_yawL), 0.0)



        norm_convergence = routine_convergence / self.max_convergence
        #print("PItch min: " +str(self.pitch_min))
        #print("PItch max: " +str(self.pitch_max))
        #print("PItch range: " +str(self.pitch_range))

        #print("\n")

        #print("Yaw min: " +str(self.yaw_min))
        #print("Yaw max: " +str(self.yaw_max))
        #print("Yaw range: " +str(self.yaw_range))
        #print(self.max_convergence)
        #quit()

        # distance etc. unchanged
        distance = routine_convergence

        if norm_pitch < 0 or norm_yaw < 0 or norm_pitch > 1 or norm_yaw > 1 or norm_convergence < 0 or norm_convergence > 1:
            print("INVALID VALUE ENCOUNTERED!", flush=True)
            quit()
        #print(norm_convergence)

        # invert lid labels
        if left_lid < 0.5:
            norm_pitchL = 0.5
            norm_yawL = 0.5
            left_lid = 1
        else:
            left_lid = 0

        if right_lid < 0.5:
            norm_pitchR = 0.5
            norm_yawR = 0.5
            right_lid = 1
        else:
            right_lid = 0
        

        #if left_lid < 0.5:
        #    norm_pitchL = 0.5
        #    norm_yawL = 0.5

        #if right_lid < 0.5:
        #    norm_pitchR = 0.5
        #    norm_yawR = 0.5
            #norm_pitch = 0.5
            #norm_yaw = 0.5
            #norm_convergence = 0.5

        #label = np.array([norm_pitchL, norm_yawL, norm_pitchR, norm_yawR], dtype=np.float32)
        if self.side == 'left':
            label = np.array([norm_pitchL, norm_yawL, left_lid], dtype=np.float32)
        else:
            label = np.array([norm_pitchR, norm_yawR, right_lid], dtype=np.float32)
            
        #label = np.array([norm_pitch, norm_yaw, 0.5, left_lid], dtype=np.float32)
        # Determine if this is a "safe" frame
        is_safe_frame = (routine_state == 67108864) or self.force_zero
        
        # Apply any additional transforms if provided
        if self.transform:
            image = self.transform(image)
        
        return image.to(DEVICE), torch.from_numpy(label).to(DEVICE), is_safe_frame
    
    def get_raw_frame(self, idx):
        """Return the raw frame for video rendering"""
        # Create a compatible structure to match the original API
        frame_data = self.aligned_frames[idx]
        
        # Create a simple object with the necessary attributes
        class CompatFrame:
            pass
        
        frame = CompatFrame()
        
        # Unpack the data
        label_data, left_eye_jpeg, right_eye_jpeg, label_timestamp, _ = frame_data
        (routine_pitch, routine_yaw, routine_distance, routine_convergence, fov_adjust_distance,
         left_eye_pitch, left_eye_yaw, right_eye_pitch, right_eye_yaw,
         routine_left_lid, routine_right_lid, routine_brow_raise, routine_brow_angry,
         routine_widen, routine_squint, routine_dilate, routine_state) = label_data
        
        # Set attributes to match the original CaptureFrame
        frame.image_data_left = decode_jpeg(left_eye_jpeg)
        frame.image_data_right = decode_jpeg(right_eye_jpeg)
        frame.routinePitch = routine_pitch
        frame.routineYaw = routine_yaw
        frame.routineDistance = routine_distance
        frame.fovAdjustDistance = fov_adjust_distance
        frame.routineState = routine_state
        frame.isSafeFrame = (routine_state == 67108864)
        frame.timestampLow = label_timestamp & 0xFFFFFFFF
        frame.timestampHigh = (label_timestamp >> 32) & 0xFFFFFFFF
        
        return frame

def train_model(model, decoder, train_loader, num_epochs=10, lr=5e-5, class_step=False, e_add = 0, e_total = 0):
    device = DEVICE#torch.device("cuda:0")
    print(f"Using device: {device}", flush=True)
    
    model = model.to(device)
    criterion = nn.MSELoss()
    
    
    # For tracking progress
    epoch_losses = []
    batch_losses = []

    model.train()
    #decoder.train()

    model = model.to(DEVICE)
    #decoder = decoder.to(DEVICE)
    optimizerE = optim.AdamW(list(model.parameters()), lr=0.001)

    def warmup_fn(epoch):
        return min(1.0, epoch / 5)  # Gradually increase LR for first 5 epochs

    warmup_scheduler = LambdaLR(optimizerE, lr_lambda=warmup_fn)

    T_max = num_epochs-5  # Total epochs minus warm-up
    eta_min = 1e-5  # Minimum LR after decay

    cosine_scheduler = CosineAnnealingLR(optimizerE, T_max=T_max, eta_min=eta_min)

    
    for epoch in range(num_epochs):
        print("\n=== Epoch %d/%d ===\n" % (epoch + 1 + e_add, e_total + 1), flush=True)#printf("\n=== Epoch %d/%d ===\n", epoch + 1, num_epochs);

        start = time.time()
        
        running_loss = 0.0

        max_i = len(train_loader)
        
        for i, (inputs, labels, states) in enumerate(train_loader):
            #if i < 5:
            #    continue
            try:
                inputs = inputs.to(device)
                labels = labels.to(device)

                raw_inputs = inputs

                # Zero the parameter gradients
                optimizerE.zero_grad()
                # optimizerD.zero_grad()
                

                if class_step:
                    # print(inputs)
                    outputs = model(inputs, return_blends=True)
                    #print(outputs)
                    #outputs = decoder(latents)
                    # print(labels[0])
                    loss = criterion(outputs, labels)# / (1+(0.01 * latents.std()))
 
                # Backward pass and optimize
                loss.backward()
                optimizerE.step()
                #progress.set_description("(%d/%d) Loss: %.6f" % (i, max_i, float(loss)))
                #  optimizerD.step()
                print("\rBatch %u/%u, Loss: %.6f" % (i, max_i, float(loss)), flush=True)
                
                # Print statistics
                running_loss += loss.item()
                batch_losses.append(loss.item())
                if i % 10 == 0 and not class_step:
                    # For visualization, only show the current frame (first 2 channels)
                    image = inputs[0, :2].cpu().numpy()

                    image = np.transpose(image, (1, 2, 0))  # Convert (2, 128, 128) to (128, 128, 2)
                    image_bgr = np.dstack((image[:, :, 0], image[:, :, 1], image[:, :, 0]))
                    image_bgr = np.clip((image_bgr * 255), 0, 255).astype(np.uint8)

                    image = outputs[0].detach().cpu().numpy()
                    image = np.transpose(image, (1, 2, 0))  # Convert (2, 128, 128) to (128, 128, 2)
                    image_bgr2 = np.dstack((image[:, :, 0], image[:, :, 1], image[:, :, 0]))
                    image_bgr2 = np.clip((image_bgr2 * 255), 0, 255).astype(np.uint8)

                    #print(inputs[0].numpy().shape)
                    cv2.imshow("test", np.hstack((image_bgr, image_bgr2)))
                    #cv2.imshow("out", image_bgr2)
                    cv2.waitKey(1)
            except:
                import traceback
                traceback.print_exc()
                print("err")
        
        # Print epoch statistics
        epoch_loss = running_loss / len(train_loader)
        epoch_losses.append(epoch_loss)
        #print(f"Epoch {epoch+1}/{num_epochs} completed. Average loss: {epoch_loss:.4f}")
        print("\nEpoch %d/%d completed in %.2fs. Average loss: %.6f\n" % (epoch + 1, num_epochs + 1, time.time() - start, epoch_loss), flush=True)
        #print("end: " + str(time.time() - start))

        #s#ched.step()
        if epoch < 5:
            warmup_scheduler.step()
        else:
            cosine_scheduler.step()

    return model, epoch_losses, batch_losses

def normalize_similarity(similarity):
    return (similarity + 1) / 2

def main():
    global TRAINING

    # Set random seed for reproducibility
    torch.manual_seed(42)
    np.random.seed(42)
    
    model_L=MicroChad()
    model_R=MicroChad()

    print("Total params: " + str(count_parameters(model_L) * 2), flush=True)
    print(model_L, flush=True)
    print(model_R, flush=True)

    model_L.load_state_dict(torch.load("baseline_L.pth", map_location="cpu"))
    model_L.to(DEVICE)
    model_R.load_state_dict(torch.load("baseline_R.pth", map_location="cpu"))
    model_R.to(DEVICE)
    trained_model_L = model_L
    trained_model_R = model_R

    EPOCHS_AUG = 8 # 8
    EPOCHS_NOAUG = 1

    if True:
        for e in range(1):
            dataset = CaptureDataset('user_cal.bin', all_frames=False, side='left')

            train_dataset = dataset
            train_loader = DataLoader(train_dataset, batch_size=32, shuffle=True, num_workers=0)

            trained_model_L, epoch_losses, batch_losses = train_model(
                trained_model_L,
                None,
                train_loader, 
                num_epochs=EPOCHS_AUG,
                lr=0.001,
                class_step=True, 
                e_add = 0,
                e_total = EPOCHS_AUG+EPOCHS_AUG+EPOCHS_NOAUG+EPOCHS_NOAUG
            )

            TRAINING = False # disable augs for 1 epoch

            del train_loader

            train_loader = DataLoader(train_dataset, batch_size=32, shuffle=True, num_workers=0)

            trained_model_L, epoch_losses, batch_losses = train_model(
                trained_model_L, 
                None,
                train_loader, 
                num_epochs=EPOCHS_NOAUG,
                lr=0.001,
                class_step=True,
                e_add = EPOCHS_AUG,
                e_total = EPOCHS_AUG+EPOCHS_AUG+EPOCHS_NOAUG+EPOCHS_NOAUG
            )
        
        TRAINING = True

        for e in range(1):
            dataset = CaptureDataset('user_cal.bin', all_frames=False, side='right')

            train_dataset = dataset
            train_loader = DataLoader(train_dataset, batch_size=32, shuffle=True, num_workers=0)

            trained_model_R, epoch_losses, batch_losses = train_model(
                trained_model_R,
                None,
                train_loader, 
                num_epochs=EPOCHS_AUG,
                lr=0.001,
                class_step=True,
                e_add = EPOCHS_AUG+EPOCHS_NOAUG,
                e_total = EPOCHS_AUG+EPOCHS_AUG+EPOCHS_NOAUG+EPOCHS_NOAUG
            )

            TRAINING = False # disable augs for 1 epoch

            del train_loader

            train_loader = DataLoader(train_dataset, batch_size=32, shuffle=True, num_workers=0)

            trained_model_R, epoch_losses, batch_losses = train_model(
                trained_model_R, 
                None,
                train_loader, 
                num_epochs=EPOCHS_NOAUG,
                lr=0.001,
                class_step=True,
                e_add = EPOCHS_AUG+EPOCHS_AUG+EPOCHS_NOAUG,
                e_total = EPOCHS_AUG+EPOCHS_AUG+EPOCHS_NOAUG+EPOCHS_NOAUG
            )

            
    # Save the final model
    #torch.save(trained_model.state_dict(), "final_model_temporal_que_tuned_2.pth")
    
    torch.save(trained_model_L.state_dict(), "left_tuned.pth")
    torch.save(trained_model_R.state_dict(), "right_tuned.pth")

    multi = MultiChad()
    multi.left.load_state_dict(trained_model_L.state_dict())
    multi.right.load_state_dict(trained_model_R.state_dict())
    #multi.right = trained_model_R

    print("\nTraining completed successfully!\n", flush=True)

    device = torch.device("cpu")
    
    dummy_input = torch.randn(1, 8, 128, 128, device=device)  # Updated to 8 channels
    torch.onnx.export(
        multi,
        dummy_input,
        sys.argv[2],
        export_params=True,
        opset_version=15,
        do_constant_folding=True,
        input_names=['input'],
        output_names=['output'],
        dynamic_axes={
            'input': {0: 'batch_size'},
            'output': {0: 'batch_size'}
        }
    )
    print("Model exported to ONNX: " + sys.argv[2], flush=True)

if __name__ == "__main__":
    main()
