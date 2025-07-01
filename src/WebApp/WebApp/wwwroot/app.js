window.blazorFolderPicker = {
    // Function to get the common folder path from selected files
    getFolderPath: (files) => {
        if (!files || files.length === 0) {
            return null;
        }

        // Get the webkitRelativePath from the first file
        const firstFile = files[0];
        if (!firstFile.webkitRelativePath) {
            return null;
        }

        // Extract the folder path by removing the file name
        const relativePath = firstFile.webkitRelativePath;
        const pathParts = relativePath.split('/');
        
        // If there's only one part, it's just a file name, no folder structure
        if (pathParts.length <= 1) {
            return null;
        }

        // Remove the last part (file name) to get the folder path
        pathParts.pop();
        const folderPath = pathParts.join('\\'); // Use Windows path separator
        
        return folderPath;
    },

    // Function to set up event listener on the file input
    setupFolderInput: (inputElement, dotNetHelper) => {
        if (!inputElement) {
            return;
        }

        inputElement.addEventListener('change', (event) => {
            const files = event.target.files;
            const folderPath = window.blazorFolderPicker.getFolderPath(files);
            
            if (folderPath && dotNetHelper) {
                dotNetHelper.invokeMethodAsync('OnFolderSelected', folderPath);
            }
        });
    }
};
