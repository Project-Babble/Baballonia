from data import CaptureDataset, DatasetWrapper, worker_init_with_file
import functools
import time
import random

import torch_directml
import babble_data

device = torch_directml.device()

FLAG_GAZE_DATA = 1 << 0
MODEL_NAMES = ["gaze", "blink", "widesqueeze", "brow"]

# batcher functions used by workers
def batcher_gaze(train_loader):
    return train_loader.get_next_gaze()

def batcher_lid(train_loader): #todo better weighting just threw this together
    f = random.random()
    if f > 0.2:
        if f > 0.9:
            return train_loader.get_next_angry()
        else:
            return train_loader.get_next_gaze()
    else:
        f = random.random()
        if f > 0.5:
            return train_loader.get_next_closed()
        elif f > 0.25:
            return train_loader.get_next_wide()
        else:
            return train_loader.get_next_squint()

def batcher_wide_squeeze(train_loader):
    f = random.random()
    if f > 0.5:
        if f > 0.9:
            return train_loader.get_next_closed()
        elif f > 0.8:
            return train_loader.get_next_angry()
        else:
            return train_loader.get_next_gaze()
    elif f > 0.25:
        return train_loader.get_next_wide()
    else:
        return train_loader.get_next_squint()
    
def batcher_brow(train_loader):
    f = random.random()
    if f > 0.7:
        if f > 0.975:
            return train_loader.get_next_closed()
        elif f > 0.95:
            return train_loader.get_next_squint()
        elif f > 0.925:
            return train_loader.get_next_wide()
        else:
            return train_loader.get_next_gaze()
        
    else:
        return train_loader.get_next_angry()


global_epoch = 0
global_epoch_count = 0

if __name__ == "__main__":
    import torch
    import torch.optim as optim
    from torch.utils.data import Dataset, DataLoader
    from torch.optim.lr_scheduler import LambdaLR, CosineAnnealingLR
    from models import MicroChad, MultiInputMergedMicroChad
    import numpy as np
    import torch.nn as nn
    import random
    import sys
    
    DEVICE = device

    #['main.py', 'user_cal.bin', 'tuned_temporal_eye_tracking.onnx']
    print("Sys argv:")
    print(sys.argv)

    BIN_FILE = sys.argv[1]

    def train_model(models_left, models_right, train_loader, lrs=[], label_sizes=[]):
        global global_epoch_count
        device = DEVICE
        print(f"Using device: {device}", flush=True)

        criterion = nn.MSELoss()

        EPOCH_SIZE = 100

        for model in models_left + models_right:
            model.train()
        models_left = [ model.to(DEVICE) for model in models_left ]
        models_right = [ model.to(DEVICE) for model in models_right ]

        optimizersL = [ optim.AdamW(list(models_left[e].parameters()), lr=lrs[e]) for e in range(len(models_left)) ]
        optimizersR = [ optim.AdamW(list(models_right[e].parameters()), lr=lrs[e]) for e in range(len(models_right)) ]

        def train_one_model(num_epochs, steps_per_epoch, model_idx, batcher_fn):
            global global_epoch
            worker_init = functools.partial(worker_init_with_file, file=BIN_FILE)
            
            

            #babble_data.init([raw_jpeg_data_left,raw_jpeg_data_right], [0.5,0.5])

            if True:
                dataloader = DataLoader(DatasetWrapper(batcher_fn, num_epochs, 16, steps_per_epoch), batch_size=16, shuffle=True, num_workers=8, worker_init_fn=worker_init)
                
                print("\nTraining %s:" % (MODEL_NAMES[model_idx]))
                optim_left = optimizersL[model_idx]
                optim_right = optimizersR[model_idx]
                model_left = models_left[model_idx]
                model_right = models_right[model_idx]

                T_max = num_epochs-5  # Total epochs minus warm-up
                eta_min = lrs[model_idx] / 5  # Minimum LR after decay
                
                sched_left = CosineAnnealingLR(optim_left, T_max=T_max * steps_per_epoch, eta_min=eta_min)
                sched_right = CosineAnnealingLR(optim_right, T_max=T_max * steps_per_epoch, eta_min=eta_min)

                def warmup_fn(epoch):
                    return min(1.0, (epoch+1) / (5*EPOCH_SIZE))  # Gradually increase LR for first 5 epochs
                
                sched_left_w = LambdaLR(optim_left, lr_lambda=warmup_fn)
                sched_right_w = LambdaLR(optim_right, lr_lambda=warmup_fn)

                label_start_idx = 0
                for e in range(model_idx):
                    label_start_idx = label_start_idx + label_sizes[e]
                label_end_idx = label_start_idx + label_sizes[model_idx]

                train_loader.reset_use_counts()

                dataloader_iterator = iter(dataloader)

                for epoch in range(num_epochs):
                    all_loss = 0
                    start_time = time.time()
                    global_epoch = global_epoch + 1
                    print("\n=== Epoch %d/%d ===\n" % (global_epoch, global_epoch_count), flush=True)
                    for step in range(steps_per_epoch):
                        inputs_left, inputs_right, labels_left, labels_right, gaze_states, sample_states = next(dataloader_iterator)
                        
                        optim_left.zero_grad()
                        optim_right.zero_grad()

                        predL = model_left(inputs_left.to(DEVICE))
                        predR = model_right(inputs_right.to(DEVICE))

                        loss = criterion(predL, labels_left[:, label_start_idx:label_end_idx].to(DEVICE))
                        loss += criterion(predR, labels_right[:, label_start_idx:label_end_idx].to(DEVICE))

                        loss.backward()

                        optim_left.step()
                        optim_right.step()
                        loss = loss.detach()
                        print("\rBatch %u/%u, Loss: %.6f" % (step, EPOCH_SIZE, float(loss)), flush=True)#print()
                        #print("[%s(%d/%d)][%d/%d][%u/%u] loss: %.4f, lr: %.5f" % (MODEL_NAMES[model_idx], model_idx+1, len(MODEL_NAMES), epoch, num_epochs, step, EPOCH_SIZE, float(loss), float(optim_left.param_groups[0]['lr'])), flush=True)
                        all_loss += float(loss)

                        if epoch < 5:
                            sched_left_w.step()
                            sched_right_w.step()
                        #else:
                        #    sched_left.step()
                        #    sched_right.step()
                    #total_time = time.time() - start_time
                    #print(" - Epoch average: %.4f, time: %.1fs" % (all_loss / steps_per_epoch, total_time))
                    print("\nEpoch %d/%d completed in %.2fs. Average loss: %.6f\n" % (epoch + 1, num_epochs + 1, time.time() - start_time, all_loss / steps_per_epoch), flush=True)


        GAZE_EPOCHS = 48
        BLINK_EPOCHS = 32
        SQWI_EPOCHS = 32
        BROW_EPOCHS = 32

        global_epoch_count = GAZE_EPOCHS + BLINK_EPOCHS + SQWI_EPOCHS + BROW_EPOCHS

        train_one_model(GAZE_EPOCHS, EPOCH_SIZE, 0, batcher_gaze)
        train_one_model(BLINK_EPOCHS, EPOCH_SIZE, 1, batcher_lid)
        train_one_model(SQWI_EPOCHS, EPOCH_SIZE, 2, batcher_wide_squeeze)
        train_one_model(BROW_EPOCHS, EPOCH_SIZE, 3, batcher_brow)

        return models_left, models_right

    def normalize_similarity(similarity):
        return (similarity + 1) / 2

    def main():
        # Set random seed for reproducibility
        torch.manual_seed(42)
        np.random.seed(42)
        random.seed(42)
        
        model_L=[MicroChad(out_count=2), MicroChad(out_count=1), MicroChad(out_count=2), MicroChad(out_count=1)]
        model_R=[MicroChad(out_count=2), MicroChad(out_count=1), MicroChad(out_count=2), MicroChad(out_count=1)]

        trained_model_L = model_L
        trained_model_R = model_R

        dataset = CaptureDataset(BIN_FILE, all_frames=False)

        print("Sample counts: %d gaze, %d lid, %d wide, %d squint, %d brow, %d total" % (len(dataset.aligned_frames_gaze), len(dataset.aligned_frames_eyes_closed), len(dataset.aligned_frames_eyes_squinted), len(dataset.aligned_frames_eyes_wide), len(dataset.aligned_frames_brow_lowered), len(dataset.aligned_frames)))

        start_time = time.time()
        trained_model_L, trained_model_R = train_model(
            trained_model_L,
            trained_model_R,
            dataset,
            lrs=[0.001, 1e-4, 1e-4, 1e-4],
            label_sizes=[2, 1, 2, 1]
        )

        print("Training model took %d seconds." % (time.time() - start_time))

        for e in range(len(trained_model_L)):
            trained_model_L[e] = trained_model_L[e].cpu()
        for e in range(len(trained_model_R)):
            trained_model_R[e] = trained_model_R[e].cpu()

        for e in range(len(trained_model_L)):
            torch.save(trained_model_R[e].state_dict(), "right_tuned_sqwi_multiv2_%d.pth"%e)
            torch.save(trained_model_L[e].state_dict(), "left_tuned_sqwi_multiv2_%d.pth"%e)

        

        device = torch.device("cpu")
        multi = MultiInputMergedMicroChad(trained_model_L, trained_model_R)
        multi = multi.to(device)
        
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
        print("\nTraining completed successfully!\n", flush=True)

    main()
