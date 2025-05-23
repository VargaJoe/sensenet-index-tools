# SenseNet Index Validation Report
Generated: 2025. 05. 23. 11:36:13

## Summary
- Errors: 0
- Warnings: 14
- Info: 12

## Details
### [Warning] Missing SenseNet-specific fields
Details: Missing fields: NodeId

### [Warning] Document at position 0 has integrity issues
Details: Missing NodeId - Content Type: organizationalunit | Path: /root/ims/builtin/portal | Name: portal

### [Warning] Document at position 16 has integrity issues
Details: Missing NodeId - Content Type: folder | Path: /root/(apps)/folder | Name: folder

### [Warning] Document at position 32 has integrity issues
Details: Missing NodeId - Content Type: group | Path: /root/ims/builtin/portal/prcviewers | Name: prcviewers

### [Warning] Document at position 48 has integrity issues
Details: Missing NodeId - Content Type: resource | Path: /root/localization/ctdresourcesijk.xml | Name: ctdresourcesijk.xml

### [Warning] Document at position 64 has integrity issues
Details: Missing NodeId - Content Type: contenttype | Path: /root/system/schema/contenttypes/genericcontent/application/applicationoverride | Name: applicationoverride

### [Warning] Document at position 80 has integrity issues
Details: Missing NodeId - Content Type: contenttype | Path: /root/system/schema/contenttypes/genericcontent | Name: genericcontent

### [Warning] Document at position 96 has integrity issues
Details: Missing NodeId - Content Type: contenttype | Path: /root/system/schema/contenttypes/genericcontent/fieldsettingcontent/textfieldsetting/shorttextfieldsetting/choicefieldsetting/permissionchoicefieldsetting | Name: permissionchoicefieldsetting

### [Warning] Document at position 112 has integrity issues
Details: Missing NodeId - Content Type: contenttype | Path: /root/system/schema/contenttypes/genericcontent/fieldsettingcontent/xmlfieldsetting | Name: xmlfieldsetting

### [Warning] Document at position 128 has integrity issues
Details: Missing NodeId - Content Type: contenttype | Path: /root/system/schema/contenttypes/genericcontent/folder/portalroot | Name: portalroot

### [Warning] Document at position 144 has integrity issues
Details: Missing NodeId - Content Type: loggingsettings | Path: /root/system/settings/logging.settings | Name: logging.settings

### [Warning] Some documents have integrity issues
Details: Found 10 document(s) with issues out of 10 sampled

### [Warning] Content type breakdown of documents missing NodeId
Details: - contenttype: 5 document(s)
- organizationalunit: 1 document(s)
- folder: 1 document(s)
- group: 1 document(s)
- resource: 1 document(s)
- loggingsettings: 1 document(s)

### [Warning] Potential orphaned files found
Details: Files that don't match known Lucene patterns: _cong.tii

### [Info] Index directory structure verified
Details: Directory contains 10 files

### [Info] Segments file found
Details: Current segments file: segments.gen

### [Info] Multiple segments files found
Details: Found 2 segments files, current is segments.gen

### [Info] Index is not locked
Details: The index is not currently locked

### [Info] Successfully opened index with IndexReader
Details: Index contains 163 documents, maximum doc ID: 163

### [Info] Commit user data found
Details: Commit data contains 1 entries

### [Info] LastActivityId found and is valid
Details: LastActivityId = 0

### [Info] Commit user data details
Details: LastActivityId=0

### [Info] Index field structure
Details: Index contains 153 unique field names

### [Info] SenseNet commit fields are present
Details: Found both $#COMMIT and $#DATA fields

### [Info] Commit documents found
Details: Found 1 commit document(s)

### [Info] Index appears to have a single segment
Details: The index reader is not a MultiReader

